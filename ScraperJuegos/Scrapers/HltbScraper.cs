using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScraperJuegos;

/// <summary>
/// How Long To Beat: tiempo para completar el juego (horas_completo = "Completionist" / 100 %).
///
/// DECISIÓN DE DISEÑO (documentar en el informe): el robots.txt de HLTB prohíbe /api (Disallow: /api), que es el buscador
/// interno. Por eso este scraper NO usa el buscador: lee únicamente la página pública del juego,
/// https://howlongtobeat.com/game/ID, que robots.txt SÍ permite (ninguna regla aplica a esa ruta).
/// El hltb_id (el número de esa URL) es un dato del REPO, igual que steam_appid: se escribe una vez por juego en
/// Data/juegos.csv (las demás plataformas del mismo juego lo heredan). Los TIEMPOS no se guardan: se leen en vivo de la página
/// en cada consulta, y cada resultado lleva fecha_consulta real (DateTimeOffset.UtcNow).
/// Si un juego no tiene hltb_id se devuelve no_encontrado con un mensaje claro; nunca se intenta /api ni se adivina.
/// Sin estado compartido: cada llamada es independiente (seguro en paralelo).
/// </summary>
public sealed class HltbScraper : IHltbScraper
{
    private readonly ClienteHttp _http;
    public const string Fuente = "HowLongToBeat";
    private const string Base = "https://howlongtobeat.com";

    private static readonly Regex BloqueNextData = new(
        "<script[^>]*id=\"__NEXT_DATA__\"[^>]*>(?<json>.*?)</script>", RegexOptions.Singleline | RegexOptions.Compiled);

    public HltbScraper(ClienteHttp http) => _http = http;

    public Task<ResultadoHltb> ConsultarAsync(Juego juego, CancellationToken ct = default) =>
        Seguro.EjecutarAsync(() => ConsultarInternoAsync(juego, ct),
            (tipo, det) => ResultadoHltb.Fallo(tipo, det), _http.Log, juego, Fuente, ct);

    private async Task<ResultadoHltb> ConsultarInternoAsync(Juego juego, CancellationToken ct)
    {
        var ctx = new ContextoLog(juego.Nombre, juego.Plataforma, Fuente);
        if (string.IsNullOrWhiteSpace(juego.HltbId))
            throw new ScraperException(TipoError.NoEncontrado,
                $"'{juego.Nombre}' no tiene hltb_id en el REPO. HLTB prohíbe /api en su robots.txt, así que solo se lee /game/ID: " +
                "abra howlongtobeat.com, busque el juego y copie el número de su URL a la columna hltb_id (o use --hltb-id en 'probar').");
        return await PorIdAsync(juego, juego.HltbId!.Trim(), ctx, ct).ConfigureAwait(false);
    }

    private async Task<ResultadoHltb> PorIdAsync(Juego juego, string id, ContextoLog ctx, CancellationToken ct)
    {
        if (id.Length == 0 || !id.All(char.IsDigit)) throw new ScraperException(TipoError.FormatoCambiado, $"hltb_id inválido: '{id}' (debe ser solo el número de la URL)");
        string url = $"{Base}/game/{id}";
        var resp = await _http.ObtenerAsync(url, ctx, ct).ConfigureAwait(false);

        JsonElement juegoEl = ExtraerJuego(resp.Cuerpo)
            ?? throw new ScraperException(TipoError.FormatoCambiado,
                "la página del juego no trae __NEXT_DATA__ ni campos comp_main/comp_plus/comp_100 (¿cambió HLTB su página?)");

        string? nombreHltb = Texto(juegoEl, "game_name");
        if (nombreHltb is not null && !NormalizadorNombres.CoincideConJuego(juego, nombreHltb))
            throw new ScraperException(TipoError.NoEncontrado,
                $"el hltb_id {id} es '{nombreHltb}', no '{juego.Nombre}' (revise el REPO)");
        return Construir(juegoEl, url);
    }

    /// <summary>
    /// 1) Forma habitual: bloque __NEXT_DATA__ -> props.pageProps.game.data.game[0].
    /// 2) Respaldo (si HLTB cambia a otro formato de página): busca los mismos campos como texto dentro del HTML.
    /// </summary>
    internal static JsonElement? ExtraerJuego(string html)
    {
        var m = BloqueNextData.Match(html);
        if (m.Success)
        {
            try
            {
                using var doc = JsonDocument.Parse(m.Groups["json"].Value);
                if (doc.RootElement.TryGetProperty("props", out var props) && props.TryGetProperty("pageProps", out var pp)
                    && pp.TryGetProperty("game", out var g) && g.TryGetProperty("data", out var d)
                    && d.TryGetProperty("game", out var arr) && arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0)
                    return arr[0].Clone();
            }
            catch (JsonException) { /* cae al respaldo */ }
        }
        return DesdeTextoPlano(html);
    }

    // Acepta el JSON normal ("comp_main":123) y el JSON escapado dentro de un script (\"comp_main\":123).
    private static readonly Regex RxNombre = new("""game_name\\?"\s*:\s*\\?"(?<v>[^"\\]*)""", RegexOptions.Compiled);

    private static long? NumeroDe(string html, string campo)
    {
        var mm = Regex.Match(html, campo + """\\?"\s*:\s*(?<v>\d+)""");
        return mm.Success && long.TryParse(mm.Groups["v"].Value, out var n) ? n : null;
    }

    private static JsonElement? DesdeTextoPlano(string html)
    {
        long? main = NumeroDe(html, "comp_main"), plus = NumeroDe(html, "comp_plus"), c100 = NumeroDe(html, "comp_100"), all = NumeroDe(html, "comp_all");
        if (main is null && plus is null && c100 is null) return null;
        var obj = new Dictionary<string, object?> { ["comp_main"] = main ?? 0, ["comp_plus"] = plus ?? 0, ["comp_100"] = c100 ?? 0, ["comp_all"] = all ?? 0 };
        foreach (var campo in new[] { "review_score", "count_review", "count_comp", "count_playing", "count_backlog", "count_retired", "release_world" })
            if (NumeroDe(html, campo) is long v) obj[campo] = v;
        var nm = RxNombre.Match(html);
        if (nm.Success) obj["game_name"] = nm.Groups["v"].Value;
        using var d = JsonDocument.Parse(JsonSerializer.Serialize(obj));
        return d.RootElement.Clone();
    }

    private static ResultadoHltb Construir(JsonElement g, string enlace)
    {
        double? Horas(string campo) =>
            g.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var s) && s > 0
                ? Math.Round(s / 3600.0, 1) : null;
        int? Entero(string campo) =>
            g.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) && n > 0 ? n : null;

        if (!g.TryGetProperty("comp_main", out _) && !g.TryGetProperty("comp_100", out _) && !g.TryGetProperty("comp_plus", out _))
            throw new ScraperException(TipoError.FormatoCambiado, "el juego no trae campos comp_main/comp_plus/comp_100");

        double? completo = Horas("comp_100"), principal = Horas("comp_main"), extra = Horas("comp_plus"), todos = Horas("comp_all");
        if (completo is null && principal is null && extra is null && todos is null)
            throw new ScraperException(TipoError.NoEncontrado, "HLTB conoce el juego pero aún no tiene tiempos registrados");

        string? imagen = Texto(g, "game_image");
        if (!string.IsNullOrWhiteSpace(imagen) && !imagen!.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            imagen = Base + "/games/" + imagen.TrimStart('/');

        // Todo es opcional: lo que HLTB no publique queda en null (N/D), nunca rompe la consulta.
        return ResultadoHltb.Exito(completo, principal, extra, enlace) with
        {
            HorasTodosLosEstilos = todos,
            NombreHltb = Texto(g, "game_name"),
            Desarrollador = Texto(g, "profile_dev"),
            Plataformas = Texto(g, "profile_platform"),
            AnioLanzamiento = Entero("release_world"),
            CalificacionUsuarios = Entero("review_score"),
            NumResenas = Entero("count_review"),
            Completaron = Entero("count_comp"),
            Jugando = Entero("count_playing"),
            Pendiente = Entero("count_backlog"),
            Abandonaron = Entero("count_retired"),
            ImagenUrl = imagen
        };
    }

    private static string? Texto(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
