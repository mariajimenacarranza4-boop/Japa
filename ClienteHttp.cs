using System.Net;

namespace ScraperJuegos;

/// <summary>Respuesta HTTP ya validada (200 OK) con su cuerpo de texto.</summary>
public record RespuestaHttp(int Codigo, string Cuerpo, string UrlFinal);

/// <summary>
/// Cliente HTTP de todos los scrapers. Centraliza la robustez para que cada scraper solo se ocupe de interpretar datos:
///  1. Respeta robots.txt (si no lo permite -> bloqueo, sin hacer la solicitud).
///  2. Espera su turno en el limitador por dominio (límite de concurrencia + pausa).
///  3. Timeout por intento y reintentos con espera creciente (1s, 2s, 4s + jitter); respeta Retry-After.
///  4. Clasifica: 404 -> no_encontrado | 403/429/captcha -> bloqueo | red/5xx/timeout -> timeout.
///  5. Registra cada reintento y fallo en el log (juego, fuente, tipo, hora).
/// Es seguro entre hilos: HttpClient es thread-safe y no se guarda estado por solicitud.
/// </summary>
public sealed class ClienteHttp
{
    private readonly HttpClient _http;
    private readonly ConfigScraping _cfg;
    private readonly ILimitadorSolicitudes _limitador;
    private readonly ILogFallos _log;
    private readonly RobotsTxt _robots;

    // Ritmo adaptativo: si un sitio responde 429 (demasiadas solicitudes), se DUPLICA la pausa mínima de ese dominio
    // para TODOS los hilos (2 s, 4 s, hasta 5 s). Así se hace caso al sitio en vez de seguir chocando con el límite.
    // Tope de 5 s: con 8–10 s una corrida completa pasaba de 100 minutos (≈660 solicitudes a Eneba), con 5 s queda en ≈1 h.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, TimeSpan> _pausaPor429 = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan PausaMaxPor429 = TimeSpan.FromSeconds(5);

    public ConfigScraping Config => _cfg;
    public ILogFallos Log => _log;

    public ClienteHttp(HttpClient http, ConfigScraping cfg, ILimitadorSolicitudes limitador, ILogFallos log)
    {
        _http = http; _cfg = cfg; _limitador = limitador; _log = log;
        _robots = new RobotsTxt(http, cfg, limitador);
    }

    /// <summary>Crea un cliente real (para producción).</summary>
    public static ClienteHttp Crear(ConfigScraping? cfg = null, ILimitadorSolicitudes? limitador = null, ILogFallos? log = null)
    {
        cfg ??= new ConfigScraping();
        var handler = new SocketsHttpHandler
        {
            UseCookies = false, // sin cookies: scraper sin estado; evita reenviar cookies con caracteres no ASCII (error "Request headers must contain only ASCII characters")
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5
        };
        var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan }; // el timeout lo manejamos por intento
        return new ClienteHttp(http, cfg, limitador ?? new LimitadorDominio(), log ?? new LogNulo());
    }

    public Task<RespuestaHttp> ObtenerAsync(string url, ContextoLog ctx, CancellationToken ct,
        IDictionary<string, string>? cabeceras = null) =>
        EnviarAsync(() =>
        {
            var r = new HttpRequestMessage(HttpMethod.Get, url);
            if (cabeceras != null) foreach (var kv in cabeceras) r.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
            return r;
        }, new Uri(url), ctx, ct);

    /// <summary>Envía una solicitud (la fábrica crea una nueva en cada reintento, porque un HttpRequestMessage no se reutiliza).</summary>
    public async Task<RespuestaHttp> EnviarAsync(Func<HttpRequestMessage> fabrica, Uri uri, ContextoLog ctx, CancellationToken ct)
    {
        string ultimoDetalle = "";
        string tipoFinal = TipoError.Timeout;

        for (int intento = 0; intento <= _cfg.MaxReintentos; intento++)
        {
            ct.ThrowIfCancellationRequested();

            if (_cfg.RespetarRobots)
            {
                var (permiteRobots, motivoRobots) = await _robots.ExplicarAsync(uri, ct).ConfigureAwait(false);
                if (!permiteRobots)
                    throw new ScraperException(TipoError.Bloqueo, $"robots.txt no permite {uri.Host}{uri.AbsolutePath} -> {motivoRobots}");
            }

            TimeSpan? esperaServidor = null;
            try
            {
                using var turno = await _limitador.EntrarAsync(uri.Host, ct).ConfigureAwait(false);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(_cfg.Timeout);
                using var req = fabrica();
                req.Headers.TryAddWithoutValidation("User-Agent", _cfg.UserAgent);
                if (!req.Headers.Contains("Accept-Language")) req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.8");

                using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, cts.Token).ConfigureAwait(false);
                int code = (int)resp.StatusCode;

                if (code == 404 || code == 410)
                    throw new ScraperException(TipoError.NoEncontrado, $"HTTP {code} en {uri.PathAndQuery}");
                if (code == 403 || code == 401)
                    throw new ScraperException(TipoError.Bloqueo, $"HTTP {code}: acceso denegado (no se reintenta)");
                if (code == 429)
                {
                    esperaServidor = resp.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5);
                    tipoFinal = TipoError.Bloqueo; ultimoDetalle = "HTTP 429: demasiadas solicitudes";
                    BajarRitmo(uri.Host, ctx);
                }
                else if (code >= 500)
                {
                    tipoFinal = TipoError.Timeout; ultimoDetalle = $"HTTP {code}: error del servidor";
                }
                else if (code >= 200 && code < 300)
                {
                    string cuerpo = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                    if (PareceBloqueo(cuerpo))
                        throw new ScraperException(TipoError.Bloqueo, "la página parece un desafío anti-bots/captcha");
                    return new RespuestaHttp(code, cuerpo, resp.RequestMessage?.RequestUri?.ToString() ?? uri.ToString());
                }
                else
                    throw new ScraperException(TipoError.FormatoCambiado, $"HTTP {code} inesperado");
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                tipoFinal = TipoError.Timeout; ultimoDetalle = $"timeout de {_cfg.Timeout.TotalSeconds:0}s";
            }
            catch (HttpRequestException ex)
            {
                tipoFinal = TipoError.Timeout; ultimoDetalle = "error de conexión: " + ex.Message;
            }

            if (intento < _cfg.MaxReintentos)
            {
                var espera = esperaServidor ?? TimeSpan.FromMilliseconds(
                    _cfg.EsperaBase.TotalMilliseconds * Math.Pow(2, intento) + Random.Shared.Next(0, 250));
                if (espera > _cfg.EsperaMaxima) espera = _cfg.EsperaMaxima;
                _log.Registrar(ctx.Juego, ctx.Plataforma, ctx.Fuente, "reintento",
                    $"intento {intento + 1}/{_cfg.MaxReintentos + 1} falló ({ultimoDetalle}); espera {espera.TotalSeconds:0.0}s");
                await Task.Delay(espera, ct).ConfigureAwait(false);
            }
        }
        throw new ScraperException(tipoFinal, $"{ultimoDetalle} (tras {_cfg.MaxReintentos + 1} intentos)");
    }

    private static bool PareceBloqueo(string cuerpo)
    {
        if (cuerpo.Length > 20000) return false; // las páginas reales son grandes; los desafíos son cortos
        return cuerpo.Contains("cf-browser-verification", StringComparison.OrdinalIgnoreCase)
            || cuerpo.Contains("Just a moment...", StringComparison.OrdinalIgnoreCase)
            || cuerpo.Contains("g-recaptcha", StringComparison.OrdinalIgnoreCase)
            || cuerpo.Contains("Access Denied", StringComparison.OrdinalIgnoreCase)
            || cuerpo.Contains("Are you a robot", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Tras un 429, duplica la pausa mínima del dominio (compartida por todos los hilos). Nunca la baja.</summary>
    private void BajarRitmo(string host, ContextoLog ctx)
    {
        var nueva = _pausaPor429.AddOrUpdate(host, TimeSpan.FromSeconds(2),
            (_, actual) => actual * 2 > PausaMaxPor429 ? PausaMaxPor429 : actual * 2);
        _limitador.ExigirPausaMinima(host, nueva);
        _log.Registrar(ctx.Juego, ctx.Plataforma, ctx.Fuente, "ritmo",
            $"{host} pidió ir más lento (HTTP 429): pausa mínima entre solicitudes ahora {nueva.TotalSeconds:0} s");
    }
}
