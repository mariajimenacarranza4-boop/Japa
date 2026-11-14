using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace ScraperJuegos;

/// <summary>Reglas de un robots.txt ya interpretadas para nuestro User-Agent.</summary>
public sealed class ReglasRobots
{
    private readonly List<(bool permite, string patron)> _reglas = new();
    public TimeSpan? CrawlDelay { get; private set; }
    public bool PermiteTodo { get; init; }
    public bool BloqueaTodo { get; init; }
    /// <summary>Por qué se llegó a estas reglas (robots.txt leído, 404 sin robots, 5xx, excepción...). Sirve para el diagnóstico y los mensajes.</summary>
    public string Motivo { get; init; } = "";
    /// <summary>true si se bloquea por un fallo PASAJERO al leer robots.txt (timeout, 5xx): no debe recordarse para toda la corrida.</summary>
    public bool Transitorio { get; init; }

    public static ReglasRobots TodoPermitido(string motivo = "") => new() { PermiteTodo = true, Motivo = motivo };
    public static ReglasRobots TodoBloqueado(string motivo = "", bool transitorio = false) =>
        new() { BloqueaTodo = true, Motivo = motivo, Transitorio = transitorio };

    /// <summary>Interpreta el texto (RFC 9309): elige el grupo de nuestro token, o "*" si no hay.</summary>
    public static ReglasRobots Parsear(string texto, string token)
    {
        var grupos = new List<(List<string> agentes, List<(bool, string)> reglas, TimeSpan? delay)>();
        List<string>? agentes = null; List<(bool, string)>? reglas = null; TimeSpan? delay = null;
        bool ultimaFueAgente = false;

        foreach (var cruda in texto.Replace("\r", "").Split('\n'))
        {
            string linea = cruda.Split('#')[0].Trim();
            if (linea.Length == 0) continue;
            int p = linea.IndexOf(':');
            if (p < 0) continue;
            string clave = linea[..p].Trim().ToLowerInvariant();
            string valor = linea[(p + 1)..].Trim();

            if (clave == "user-agent")
            {
                if (!ultimaFueAgente || agentes == null)
                {
                    if (agentes != null) grupos.Add((agentes, reglas!, delay));
                    agentes = new List<string>(); reglas = new List<(bool, string)>(); delay = null;
                }
                agentes.Add(valor.ToLowerInvariant());
                ultimaFueAgente = true;
                continue;
            }
            ultimaFueAgente = false;
            if (agentes == null) continue;
            if (clave == "allow") reglas!.Add((true, valor));
            else if (clave == "disallow") { if (valor.Length > 0) reglas!.Add((false, valor)); }
            else if (clave == "crawl-delay" && double.TryParse(valor, System.Globalization.NumberStyles.Float,
                         System.Globalization.CultureInfo.InvariantCulture, out double s))
                delay = TimeSpan.FromSeconds(s);
        }
        if (agentes != null) grupos.Add((agentes, reglas!, delay));

        string tk = token.ToLowerInvariant();
        var elegido = grupos.FirstOrDefault(g => g.agentes.Any(a => a != "*" && tk.Contains(a)));
        if (elegido.agentes == null) elegido = grupos.FirstOrDefault(g => g.agentes.Contains("*"));
        if (elegido.agentes == null) return TodoPermitido("robots.txt leído: no tiene reglas para nuestro agente ni para *");
        var r = new ReglasRobots { Motivo = "robots.txt leído" };
        foreach (var x in elegido.reglas) r._reglas.Add(x);
        r.CrawlDelay = elegido.delay;
        return r;
    }

    /// <summary>La regla más específica (patrón más largo) gana; en empate gana Allow.</summary>
    public bool Permite(string rutaYQuery) => Explicar(rutaYQuery).permite;

    /// <summary>Igual que Permite, pero dice QUÉ regla decidió (o por qué se bloquea todo).</summary>
    public (bool permite, string motivo) Explicar(string rutaYQuery)
    {
        if (BloqueaTodo) return (false, Motivo);
        if (PermiteTodo) return (true, Motivo);
        int mejorLargo = -1; bool permitido = true; string regla = "ninguna regla aplica a esa ruta";
        foreach (var (permite, patron) in _reglas)
        {
            if (!Coincide(patron, rutaYQuery)) continue;
            if (patron.Length > mejorLargo || (patron.Length == mejorLargo && permite))
            { mejorLargo = patron.Length; permitido = permite; regla = (permite ? "Allow " : "Disallow ") + patron; }
        }
        return (permitido, "regla que decide: " + regla);
    }

    private static bool Coincide(string patron, string ruta)
    {
        bool anclado = patron.EndsWith('$');
        if (anclado) patron = patron[..^1];
        string rx = "^" + Regex.Escape(patron).Replace("\\*", ".*") + (anclado ? "$" : "");
        return Regex.IsMatch(ruta, rx);
    }
}

/// <summary>
/// Descarga y cachea (por dominio) el robots.txt. La caché es un recurso compartido: ConcurrentDictionary de
/// Lazy&lt;Task&gt; para que, aunque 20 hilos pidan el mismo dominio a la vez, se descargue UNA sola vez.
/// </summary>
public sealed class RobotsTxt
{
    private readonly HttpClient _http;
    private readonly ConfigScraping _cfg;
    private readonly ILimitadorSolicitudes _limitador;
    private readonly ConcurrentDictionary<string, Lazy<Task<ReglasRobots>>> _cache = new(StringComparer.OrdinalIgnoreCase);

    public RobotsTxt(HttpClient http, ConfigScraping cfg, ILimitadorSolicitudes limitador)
    {
        _http = http; _cfg = cfg; _limitador = limitador;
    }

    public async Task<ReglasRobots> ObtenerAsync(Uri uri, CancellationToken ct)
    {
        string clave = uri.GetLeftPart(UriPartial.Authority);
        var lazy = _cache.GetOrAdd(clave, k => new Lazy<Task<ReglasRobots>>(() => DescargarAsync(k)));
        var reglas = await lazy.Value.WaitAsync(ct).ConfigureAwait(false);
        // Un fallo pasajero al leer robots.txt (timeout, 5xx) NO se recuerda: la próxima solicitud lo vuelve a intentar.
        if (reglas.Transitorio) _cache.TryRemove(new KeyValuePair<string, Lazy<Task<ReglasRobots>>>(clave, lazy));
        if (reglas.CrawlDelay is { } d) _limitador.ExigirPausaMinima(uri.Host, d);
        return reglas;
    }

    private async Task<ReglasRobots> DescargarAsync(string autoridad)
    {
        try
        {
            using var turno = await _limitador.EntrarAsync(new Uri(autoridad).Host).ConfigureAwait(false);
            using var cts = new CancellationTokenSource(_cfg.Timeout);
            using var req = new HttpRequestMessage(HttpMethod.Get, autoridad + "/robots.txt");
            req.Headers.TryAddWithoutValidation("User-Agent", _cfg.UserAgent);
            using var resp = await _http.SendAsync(req, cts.Token).ConfigureAwait(false);
            int code = (int)resp.StatusCode;
            if (code >= 200 && code < 300)
                return ReglasRobots.Parsear(await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false), _cfg.TokenRobots);
            if (code >= 400 && code < 500)
                return ReglasRobots.TodoPermitido($"HTTP {code} al pedir robots.txt: se trata como sin restricciones (RFC 9309)");
            return ReglasRobots.TodoBloqueado($"HTTP {code} al pedir robots.txt (error del servidor): por prudencia no se pide nada", transitorio: true);
        }
        catch (Exception ex)
        {
            // No pudimos leerlo (sin red, timeout, TLS...): preferimos no pedir nada, pero sin recordarlo para siempre.
            return ReglasRobots.TodoBloqueado($"no se pudo leer robots.txt ({ex.GetType().Name}: {ex.Message})", transitorio: true);
        }
    }

    public async Task<bool> PermiteAsync(Uri uri, CancellationToken ct)
    {
        var r = await ObtenerAsync(uri, ct).ConfigureAwait(false);
        return r.Permite(uri.PathAndQuery);
    }

    /// <summary>Como PermiteAsync, pero con el motivo (qué regla decidió, o por qué no se pudo leer robots.txt).</summary>
    public async Task<(bool permite, string motivo)> ExplicarAsync(Uri uri, CancellationToken ct)
    {
        var r = await ObtenerAsync(uri, ct).ConfigureAwait(false);
        return r.Explicar(uri.PathAndQuery);
    }
}
