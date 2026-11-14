using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScraperJuegos;

/// <summary>
/// Metacritic: score de la CONSOLA asociada en el REPO (no un score genérico).
/// URL: https://www.metacritic.com/game/{slug}/?platform={plataforma}
///  - slug: columna metacritic_slug del REPO o, si está vacía, se genera del nombre.
///  - Estrategia de lectura (de más a menos estable): 1) JSON-LD (aggregateRating.ratingValue),
///    2) HTML del componente c-siteReviewScore.
///  - Plataforma: si el JSON-LD trae gamePlatform y NO coincide con la pedida -> no_encontrado (no se adivina).
///    Si la página no permite confirmarla, se devuelve el score con verificada=false para que se pueda auditar.
/// Es la fuente más frágil del proyecto (el sitio cambia y suele bloquear bots): VALIDAR EN VIVO con "probar".
/// </summary>
public sealed class MetacriticScraper : IMetacriticScraper
{
    private readonly ClienteHttp _http;
    public const string Fuente = "Metacritic";

    private static readonly Dictionary<string, (string slug, string[] nombres)> Plataformas = new()
    {
        ["PS5"] = ("playstation-5", new[] { "playstation 5", "ps5" }),
        ["PS4"] = ("playstation-4", new[] { "playstation 4", "ps4" }),
        ["Xbox Series"] = ("xbox-series-x", new[] { "xbox series x", "xbox series" }),
        ["Xbox One"] = ("xbox-one", new[] { "xbox one" }),
        ["PC"] = ("pc", new[] { "pc", "windows" }),
        ["Switch"] = ("switch", new[] { "nintendo switch", "switch" }),
    };

    private static readonly Regex JsonLd = new(
        "<script[^>]*type=\"application/ld\\+json\"[^>]*>(?<json>.*?)</script>", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex ScoreHtml = new(
        "c-siteReviewScore[^>]*>\\s*<span[^>]*>\\s*(?<n>\\d{1,3})\\s*</span>", RegexOptions.Singleline | RegexOptions.Compiled);

    public MetacriticScraper(ClienteHttp http) => _http = http;

    public Task<ResultadoMetacritic> ConsultarAsync(Juego juego, CancellationToken ct = default) =>
        Seguro.EjecutarAsync(() => ConsultarInternoAsync(juego, ct),
            (tipo, det) => ResultadoMetacritic.Fallo(tipo, det), _http.Log, juego, Fuente, ct);

    private async Task<ResultadoMetacritic> ConsultarInternoAsync(Juego juego, CancellationToken ct)
    {
        if (!Plataformas.TryGetValue(juego.Plataforma, out var plat))
            throw new ScraperException(TipoError.NoEncontrado, $"plataforma '{juego.Plataforma}' sin equivalente en Metacritic");

        string slug = !string.IsNullOrWhiteSpace(juego.MetacriticSlug)
            ? juego.MetacriticSlug!.Trim() : NormalizadorNombres.Slug(juego.NombreParaBuscar);
        string url = $"https://www.metacritic.com/game/{Uri.EscapeDataString(slug)}/?platform={plat.slug}";

        var ctx = new ContextoLog(juego.Nombre, juego.Plataforma, Fuente);
        var resp = await _http.ObtenerAsync(url, ctx, ct).ConfigureAwait(false);

        int? score = null; bool? verificada = null; string? nombrePagina = null; string? fechaPagina = null;
        foreach (Match m in JsonLd.Matches(resp.Cuerpo))
        {
            try
            {
                using var doc = JsonDocument.Parse(m.Groups["json"].Value);
                if (!BuscarVideoGame(doc.RootElement, out var vg)) continue;
                nombrePagina = vg.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;

                if (vg.TryGetProperty("gamePlatform", out var gp))
                {
                    var plataformasPagina = gp.ValueKind == JsonValueKind.Array
                        ? gp.EnumerateArray().Select(x => x.ToString()).ToList() : new List<string> { gp.ToString() };
                    verificada = plataformasPagina.Any(x => plat.nombres.Any(nm =>
                        x.Contains(nm, StringComparison.OrdinalIgnoreCase)));
                    if (verificada == false)
                        throw new ScraperException(TipoError.NoEncontrado,
                            $"la página es de {string.Join('/', plataformasPagina)}, no de {juego.Plataforma}");
                }
                if (vg.TryGetProperty("datePublished", out var dp) && dp.ValueKind == JsonValueKind.String
                    && DateTime.TryParse(dp.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                                         System.Globalization.DateTimeStyles.None, out var fp))
                    fechaPagina = fp.ToString("yyyy-MM-dd");
                if (vg.TryGetProperty("aggregateRating", out var ar) && ar.TryGetProperty("ratingValue", out var rv))
                    score = ParseScore(rv);
                if (score != null) break;
            }
            catch (JsonException) { /* bloque JSON-LD ajeno o dañado: se ignora y se prueba el siguiente */ }
        }

        if (nombrePagina != null && !NormalizadorNombres.CoincideConJuego(juego, nombrePagina)
            && !NormalizadorNombres.Normalizar(nombrePagina).StartsWith(NormalizadorNombres.Normalizar(juego.NombreParaBuscar)))
            throw new ScraperException(TipoError.NoEncontrado,
                $"la página '{nombrePagina}' no corresponde a '{juego.Nombre}'");

        if (score == null)
        {
            var h = ScoreHtml.Match(resp.Cuerpo);
            if (h.Success) score = int.Parse(h.Groups["n"].Value);
        }

        if (score == null)
        {
            if (resp.Cuerpo.Contains("tbd", StringComparison.OrdinalIgnoreCase) && nombrePagina != null)
                throw new ScraperException(TipoError.NoEncontrado, "el juego existe pero aún no tiene Metascore (tbd)");
            throw new ScraperException(TipoError.FormatoCambiado, "no se encontró el Metascore (ni JSON-LD ni c-siteReviewScore)");
        }
        if (score < 0 || score > 100)
            throw new ScraperException(TipoError.FormatoCambiado, $"Metascore fuera de rango: {score}");

        // verificada = true solo si el JSON-LD confirmó la plataforma; en cualquier otro caso queda auditable.
        return ResultadoMetacritic.Exito(score.Value, juego.Plataforma, verificada == true, url, fechaPagina);
    }

    private static int? ParseScore(JsonElement v)
    {
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return (int)Math.Round(d);
        if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(),
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s)) return (int)Math.Round(s);
        return null;
    }

    private static bool BuscarVideoGame(JsonElement e, out JsonElement encontrado)
    {
        encontrado = default;
        if (e.ValueKind == JsonValueKind.Array)
        {
            foreach (var x in e.EnumerateArray()) if (BuscarVideoGame(x, out encontrado)) return true;
        }
        else if (e.ValueKind == JsonValueKind.Object)
        {
            if (e.TryGetProperty("@type", out var t) && t.ToString().Contains("VideoGame")) { encontrado = e; return true; }
            if (e.TryGetProperty("@graph", out var g)) return BuscarVideoGame(g, out encontrado);
        }
        return false;
    }
}
