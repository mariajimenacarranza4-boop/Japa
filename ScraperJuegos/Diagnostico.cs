using System.Net;
using System.Text;

namespace ScraperJuegos;

/// <summary>
/// dotnet run -- diagnostico [--hltb-id NUMERO]
///
/// Sirve para saber, con evidencia, POR QUÉ falla cada fuente (sin adivinar):
///  1. Descarga el robots.txt de cada sitio y muestra el código HTTP, las reglas y si las rutas que usamos
///     están PERMITIDAS o BLOQUEADAS (y por cuál regla). Así se distingue "el sitio lo prohíbe" de "no se pudo leer".
///  2. Guarda las respuestas CRUDAS (HTML/JSON) de Eneba, Steam y Metacritic en salida/diagnostico/,
///     para poder ver exactamente qué devuelve el sitio y corregir el parser.
///  3. Corre las 4 tiendas + Metacritic + HLTB sobre dos juegos (uno de PC y uno de PS5) y resume el resultado.
///
/// Todo respeta las mismas reglas del scraper (robots.txt, User-Agent honesto, pausa de 1 s por dominio).
/// NO intenta saltarse ningún bloqueo: solo lo mide y lo informa.
/// Escribe también salida/diagnostico/informe.txt para poder compartirlo.
/// </summary>
public static class Diagnostico
{
    private static string Hh(double? h) => h.HasValue ? h.Value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " h" : "N/D";

    private sealed record Sitio(string Nombre, string Dominio, string[] Rutas);

    // Las rutas son las mismas que piden los scrapers, pero se arman con un juego REAL del REPO (nada escrito a mano).
    private static Sitio[] ConstruirSitios(Juego m, string? hltbId, ConfigScraping cfg)
    {
        string q = Uri.EscapeDataString(m.NombreParaBuscar);
        string slug = string.IsNullOrWhiteSpace(m.MetacriticSlug) ? NormalizadorNombres.Slug(m.NombreParaBuscar) : m.MetacriticSlug!;
        var steam = new List<string> { $"/api/storesearch/?term={q}&cc=us&l=english" };
        if (!string.IsNullOrWhiteSpace(m.SteamAppId)) steam.Add($"/api/appdetails?appids={m.SteamAppId}&cc=us&l=english");
        var hltb = new List<string> { "/api/find/init?t=1", "/api/find" }; // se listan SOLO para demostrar que robots.txt las bloquea; el scraper nunca las pide
        if (!string.IsNullOrWhiteSpace(hltbId)) hltb.Add($"/game/{hltbId}");
        return new Sitio[]
        {
            new("Steam", "store.steampowered.com", steam.ToArray()),
            new("Epic vía CheapShark", "www.cheapshark.com", new[] { "/api/1.0/stores", $"/api/1.0/deals?storeID=25&title={q}&pageSize=20" }),
        new("Epic vía ITAD (respaldo)", "api.isthereanydeal.com", new[] { "/service/shops/v1", "/games/lookup/v1", "/games/prices/v3" }),
            new("Nintendo eShop (página)", "www.nintendo.com", new[] { "/us/store/products/" }),
            new("Nintendo (búsqueda Algolia)", $"{cfg.NintendoAlgoliaAppId.ToLowerInvariant()}-dsn.algolia.net", new[] { $"/1/indexes/{cfg.NintendoAlgoliaIndex}/query" }),
            new("Eneba", "www.eneba.com", new[] { $"/store/all?text={q}&currency=USD" }),
            new("Metacritic", "www.metacritic.com", new[] { $"/game/{slug}/?platform={m.Plataforma.ToLowerInvariant()}" }),
            new("HowLongToBeat", "howlongtobeat.com", hltb.ToArray()),
        };
    }

    /// <summary>
    /// dotnet run -- capturar "URL" nombre.html
    /// Descarga UNA página con el mismo cliente del scraper (robots.txt, User-Agent honesto, pausa) y la guarda cruda en
    /// salida/diagnostico/. Sirve para ver qué devuelve de verdad un sitio (por ejemplo, la página de un juego en Nintendo).
    /// </summary>
    public static async Task<int> CapturarAsync(string carpetaSalida, string? url, string? archivo)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https")
        {
            Console.WriteLine("Uso: dotnet run -- capturar \"https://...\" nombre.html");
            return 1;
        }
        string carpeta = Path.Combine(carpetaSalida, "diagnostico");
        Directory.CreateDirectory(carpeta);
        string nombre = Path.GetFileName(string.IsNullOrWhiteSpace(archivo) ? uri.Host + ".html" : archivo);
        var cliente = ClienteHttp.Crear(new ConfigScraping(), new LimitadorDominio(1, TimeSpan.FromSeconds(1)), new LogNulo());
        try
        {
            var r = await cliente.ObtenerAsync(url, new ContextoLog("captura", "-", uri.Host), CancellationToken.None);
            string ruta = Path.Combine(carpeta, nombre);
            File.WriteAllText(ruta, r.Cuerpo);
            Console.WriteLine($"OK HTTP {r.Codigo}, {r.Cuerpo.Length} caracteres -> {ruta}");
            return 0;
        }
        catch (ScraperException ex)
        {
            Console.WriteLine($"{ex.Tipo}: {ex.Message}");
            return 1;
        }
    }

    public static async Task<int> EjecutarAsync(string carpetaSalida, string? hltbId, string? rutaCsv = null)
    {
        string carpeta = Path.Combine(carpetaSalida, "diagnostico");
        Directory.CreateDirectory(carpeta);
        var cfg = new ConfigScraping();
        var informe = new StringBuilder();

        void Decir(string texto = "")
        {
            Console.WriteLine(texto);
            informe.AppendLine(texto);
        }

        // Los juegos de prueba salen del REPO (Data/juegos.csv): nombre, plataforma, fecha, precio e ids reales de ese archivo.
        var repoCarga = new RepoCsv(rutaCsv ?? Path.Combine(AppContext.BaseDirectory, "Data", "juegos.csv")).Cargar();
        var juegosRepo = repoCarga.Juegos;
        if (juegosRepo.Count == 0)
        {
            Console.WriteLine("El REPO está vacío o no se pudo leer: no hay juegos para probar. " + string.Join(" ", repoCarga.Errores));
            return 1;
        }
        Juego Elegir(string plataforma) =>
            juegosRepo.FirstOrDefault(j => j.Plataforma == plataforma && !string.IsNullOrWhiteSpace(j.HltbId))
            ?? juegosRepo.FirstOrDefault(j => j.Plataforma == plataforma)
            ?? juegosRepo[0];
        Juego muestraPc = Elegir("PC"), muestraPs = Elegir("PS5");
        if (!string.IsNullOrWhiteSpace(hltbId) && hltbId!.All(char.IsDigit)) muestraPs = muestraPs with { HltbId = hltbId.Trim() };
        hltbId = string.IsNullOrWhiteSpace(hltbId) ? (juegosRepo.FirstOrDefault(j => !string.IsNullOrWhiteSpace(j.HltbId))?.HltbId) : hltbId.Trim();
        var sitios = ConstruirSitios(muestraPs, hltbId, new ConfigScraping());

        Decir($"DIAGNÓSTICO {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} (hora real de este equipo; {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC)");
        Decir($"User-Agent: {cfg.UserAgent}");
        Decir();

        // ------------------------------------------------------------------ 1. robots.txt
        Decir("=== 1. robots.txt de cada sitio (¿permite las rutas que usamos?) ===");
        using (var handler = new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All, AllowAutoRedirect = true })
        using (var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) })
        {
            foreach (var sitio in sitios)
            {
                string url = $"https://{sitio.Dominio}/robots.txt";
                Decir();
                Decir($"--- {sitio.Nombre}  ({url})");
                ReglasRobots? reglas = null;
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.TryAddWithoutValidation("User-Agent", cfg.UserAgent);
                    using var resp = await http.SendAsync(req);
                    int codigo = (int)resp.StatusCode;
                    string texto = await resp.Content.ReadAsStringAsync();
                    string archivo = $"robots_{sitio.Dominio}.txt";
                    File.WriteAllText(Path.Combine(carpeta, archivo), texto);
                    Decir($"  HTTP {codigo}, {texto.Length} caracteres (guardado en diagnostico/{archivo})");

                    if (codigo >= 200 && codigo < 300)
                        reglas = ReglasRobots.Parsear(texto, cfg.TokenRobots);
                    else if (codigo >= 400 && codigo < 500)
                        reglas = ReglasRobots.TodoPermitido($"HTTP {codigo}: se trata como sin restricciones (RFC 9309)");
                    else
                        reglas = ReglasRobots.TodoBloqueado($"HTTP {codigo}: error del servidor", transitorio: true);

                    var lineasClave = texto.Replace("\r", "").Split('\n')
                        .Select(l => l.Trim())
                        .Where(l => l.StartsWith("user-agent", StringComparison.OrdinalIgnoreCase)
                                 || l.StartsWith("disallow", StringComparison.OrdinalIgnoreCase)
                                 || l.StartsWith("allow", StringComparison.OrdinalIgnoreCase)
                                 || l.StartsWith("crawl-delay", StringComparison.OrdinalIgnoreCase))
                        .Take(25).ToList();
                    foreach (string l in lineasClave) Decir("    | " + l);
                    if (lineasClave.Count == 25) Decir("    | ... (ver el archivo guardado para el resto)");
                }
                catch (Exception ex)
                {
                    Decir($"  NO SE PUDO LEER: {ex.GetType().Name}: {ex.Message}");
                }

                foreach (string ruta in sitio.Rutas)
                {
                    if (reglas == null) { Decir($"  ???         {ruta}  [sin robots.txt legible: el scraper la trataría como BLOQUEADA]"); continue; }
                    var (permite, motivo) = reglas.Explicar(ruta);
                    Decir($"  {(permite ? "PERMITIDO " : "BLOQUEADO ")}  {ruta}  [{motivo}]");
                }
            }
        }

        // ------------------------------------------------------------------ 2. capturas crudas
        Decir();
        Decir("=== 2. Respuestas crudas guardadas (para corregir parsers) ===");
        var limitador = new LimitadorDominio(1, TimeSpan.FromSeconds(1));
        var cliente = ClienteHttp.Crear(cfg, limitador, new LogNulo());

        async Task Capturar(string nombre, string url, string archivo)
        {
            try
            {
                var r = await cliente.ObtenerAsync(url, new ContextoLog("diagnostico", "-", nombre), CancellationToken.None);
                File.WriteAllText(Path.Combine(carpeta, archivo), r.Cuerpo);
                Decir($"  {nombre}: OK HTTP {r.Codigo}, {r.Cuerpo.Length} caracteres -> diagnostico/{archivo}");
            }
            catch (ScraperException ex)
            {
                Decir($"  {nombre}: {ex.Tipo} - {ex.Message}");
            }
            catch (Exception ex)
            {
                Decir($"  {nombre}: {ex.GetType().Name} - {ex.Message}");
            }
        }

        string qs = Uri.EscapeDataString(muestraPs.NombreParaBuscar);
        string slugMuestra = string.IsNullOrWhiteSpace(muestraPs.MetacriticSlug) ? NormalizadorNombres.Slug(muestraPs.NombreParaBuscar) : muestraPs.MetacriticSlug!;
        Decir($"  (juego de muestra tomado del REPO: {muestraPs.Nombre} / {muestraPs.Plataforma})");
        await Capturar("Steam (búsqueda)", $"https://store.steampowered.com/api/storesearch/?term={qs}&cc=us&l=english", "steam_busqueda.json");
        await Capturar("CheapShark (lista de tiendas; ahí se ve el id de Epic)", "https://www.cheapshark.com/api/1.0/stores", "cheapshark_tiendas.json");
        await Capturar("Eneba (búsqueda)", $"https://www.eneba.com/store/all?text={qs}&currency=USD", "eneba_busqueda.html");
        await Capturar($"Metacritic ({muestraPs.Nombre} {muestraPs.Plataforma})", $"https://www.metacritic.com/game/{slugMuestra}/?platform={muestraPs.Plataforma.ToLowerInvariant()}", "metacritic_muestra.html");
        if (!string.IsNullOrWhiteSpace(hltbId) && hltbId!.All(char.IsDigit))
            await Capturar($"HLTB (página del juego {hltbId})", $"https://howlongtobeat.com/game/{hltbId}", $"hltb_juego_{hltbId}.html");
        else
            Decir("  HLTB (página del juego): omitido; ningún juego del REPO tiene hltb_id (llénelo en Data/juegos.csv) o use --hltb-id N (solo el número de la URL).");

        // ------------------------------------------------------------------ 3. prueba rápida de los scrapers
        Decir();
        Decir("=== 3. Prueba rápida de los scrapers (2 juegos) ===");
        var tiendas = TiendasDisponibles.Crear(cliente);
        var meta = new MetacriticScraper(cliente);
        var hltb = new HltbScraper(cliente);
        var juegos = muestraPc.Id == muestraPs.Id ? new[] { muestraPs } : new[] { muestraPc, muestraPs };
        int fuentesOk = 0, fuentesTotal = 0;
        foreach (var j in juegos)
        {
            Decir();
            Decir($"  {j.Nombre} ({j.Plataforma})");
            foreach (var t in tiendas.Where(t => t.SoportaPlataforma(j.Plataforma)))
            {
                var r = await TiendasDisponibles.ConsultarSeguroAsync(t, j);
                fuentesTotal++;
                if (r.Ok) fuentesOk++;
                Decir($"    {t.Tienda,-18} " + (r.Ok ? $"OK  ${r.PrecioActual} (regular {(r.PrecioRegular.HasValue ? "$" + r.PrecioRegular : "N/D")})"
                                                      : $"{r.Error}: {r.Detalle}"));
            }
            var rm = await meta.ConsultarAsync(j);
            fuentesTotal++; if (rm.Ok) fuentesOk++;
            Decir($"    {"Metacritic",-18} " + (rm.Ok ? $"OK  {rm.Score}" : $"{rm.Error}: {rm.Detalle}"));
            var rh = await hltb.ConsultarAsync(j);
            fuentesTotal++; if (rh.Ok) fuentesOk++;
            Decir($"    {"HowLongToBeat",-18} " + (rh.Ok
                ? $"OK  100%={Hh(rh.HorasCompleto)} | historia={Hh(rh.HorasPrincipal)} | historia+extras={Hh(rh.HorasExtra)} | todos={Hh(rh.HorasTodosLosEstilos)}"
                : $"{rh.Error}: {rh.Detalle}"));
            if (rh.Ok)
                Decir($"      detalle HLTB: {rh.NombreHltb ?? "N/D"} | dev: {rh.Desarrollador ?? "N/D"} | año: {rh.AnioLanzamiento?.ToString() ?? "N/D"} | " +
                      $"nota usuarios: {rh.CalificacionUsuarios?.ToString() ?? "N/D"} | completaron: {rh.Completaron?.ToString() ?? "N/D"} | consultado {rh.FechaConsulta.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        }

        Decir();
        Decir($"Fuentes que respondieron bien: {fuentesOk}/{fuentesTotal}");
        Decir($"Fecha y hora al terminar (reloj de este equipo, no fija): {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        Decir("Fuente de tiempos de juego: HowLongToBeat (https://howlongtobeat.com), leída solo desde /game/ID (robots.txt prohíbe /api).");
        string rutaInforme = Path.Combine(carpeta, "informe.txt");
        File.WriteAllText(rutaInforme, informe.ToString());
        Decir($"Informe completo: {rutaInforme}");
        Decir("Carpeta con las capturas: " + carpeta);
        return 0;
    }
}
