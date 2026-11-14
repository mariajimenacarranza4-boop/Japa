using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ScraperJuegos;

/// <summary>
/// Tienda 3: Nintendo eShop (Switch, formato digital), región US (USD).
/// Nintendo no tiene API pública: la búsqueda de nintendo.com consulta un índice de Algolia con una clave de SOLO BÚSQUEDA
/// que el propio sitio entrega a todo visitante. Se consulta ese mismo índice (POST .../1/indexes/{índice}/query).
///  - Campos usados de cada resultado: title, nsuid, url, platform, msrp (precio regular), salePrice (precio actual si hay oferta).
///  - La clave PÚBLICA puede ROTAR: si cambia, Algolia responde 403 y el scraper devuelve "bloqueo" (error controlado).
///    Se puede actualizar SIN recompilar con las variables de entorno NINTENDO_ALGOLIA_KEY / NINTENDO_ALGOLIA_APP_ID / NINTENDO_ALGOLIA_INDEX
///    (ver ConfigScraping). No hay claves privadas en el proyecto.
/// Sin estado compartido: 1 solicitud por juego.
/// </summary>
public sealed class NintendoScraper : ITiendaScraper
{
    public string Tienda => "Nintendo eShop";
    public string Dominio => "www.nintendo.com";
    private readonly ClienteHttp _http;

    private const string BaseSitio = "https://www.nintendo.com";

    public NintendoScraper(ClienteHttp http) => _http = http;

    public bool SoportaPlataforma(string plataforma) => plataforma == "Switch";

    public Task<ResultadoTienda> ConsultarAsync(Juego juego, CancellationToken ct = default) =>
        Seguro.EjecutarAsync(() => ConsultarInternoAsync(juego, ct),
            (tipo, det) => ResultadoTienda.Fallo(Tienda, tipo, det), _http.Log, juego, Tienda, ct);

    private sealed record Candidato(string Titulo, decimal Regular, decimal Actual, bool EsSwitch2, string? Enlace, string? Imagen);

    private async Task<ResultadoTienda> ConsultarInternoAsync(Juego juego, CancellationToken ct)
    {
        if (!SoportaPlataforma(juego.Plataforma))
            throw new ScraperException(TipoError.NoEncontrado, $"Nintendo eShop solo vende para Switch (juego en {juego.Plataforma})");

        var cfg = _http.Config;
        if (string.IsNullOrWhiteSpace(cfg.NintendoAlgoliaKey) || string.IsNullOrWhiteSpace(cfg.NintendoAlgoliaAppId)
            || string.IsNullOrWhiteSpace(cfg.NintendoAlgoliaIndex))
            throw new ScraperException(TipoError.FormatoCambiado,
                "falta la configuración de la búsqueda de Nintendo (NINTENDO_ALGOLIA_KEY / _APP_ID / _INDEX)");

        var ctx = new ContextoLog(juego.Nombre, juego.Plataforma, Tienda);
        string host = cfg.NintendoAlgoliaAppId.Trim().ToLowerInvariant() + "-dsn.algolia.net";
        string url = $"https://{host}/1/indexes/{Uri.EscapeDataString(cfg.NintendoAlgoliaIndex.Trim())}/query";
        string cuerpo = JsonSerializer.Serialize(new
        {
            @params = "query=" + Uri.EscapeDataString(juego.NombreParaBuscar) + "&hitsPerPage=20"
        });

        var resp = await _http.EnviarAsync(() =>
        {
            var r = new HttpRequestMessage(HttpMethod.Post, url)
            { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };
            r.Headers.TryAddWithoutValidation("x-algolia-application-id", cfg.NintendoAlgoliaAppId.Trim());
            r.Headers.TryAddWithoutValidation("x-algolia-api-key", cfg.NintendoAlgoliaKey.Trim());
            r.Headers.TryAddWithoutValidation("Referer", BaseSitio + "/");
            r.Headers.TryAddWithoutValidation("Origin", BaseSitio);
            return r;
        }, new Uri(url), ctx, ct).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(resp.Cuerpo);
        if (doc.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty("hits", out var hits) || hits.ValueKind != JsonValueKind.Array)
            throw new ScraperException(TipoError.FormatoCambiado, "la búsqueda de Nintendo no trae el arreglo 'hits'");

        int evaluados = 0; bool algunPrecioPresente = false; bool algunaMatch = false;
        var candidatos = new List<Candidato>();
        foreach (var h in hits.EnumerateArray())
        {
            evaluados++;
            string? titulo = Texto(h, "title");
            if (!NormalizadorNombres.CoincideConJuego(juego, titulo)) continue;

            string plataforma = TextoOLista(h, "platform");
            if (plataforma.Length > 0 && !plataforma.Contains("switch", StringComparison.OrdinalIgnoreCase)) continue;
            algunaMatch = true;

            decimal? msrp = null, sale = null, final = null;
            if (h.TryGetProperty("price", out var pr) && pr.ValueKind == JsonValueKind.Object)
            {
                algunPrecioPresente = true;
                msrp = Numero(pr, "regPrice");
                sale = Numero(pr, "salePrice");
                final = Numero(pr, "finalPrice");
            }
            decimal regular = msrp is > 0 ? msrp.Value : (final is > 0 ? final.Value : 0m);
            if (regular <= 0) continue;
            decimal actual = final is > 0 ? final.Value : (sale is > 0 ? sale.Value : regular);

            candidatos.Add(new Candidato(titulo!, regular, actual,
                plataforma.Contains("switch 2", StringComparison.OrdinalIgnoreCase), Enlace(h), Imagen(h)));
        }

        if (candidatos.Count == 0)
        {
            if (!algunaMatch)
                throw new ScraperException(TipoError.NoEncontrado,
                    $"ningún resultado de Nintendo eShop coincide con '{juego.NombreParaBuscar}' para Switch (se evaluaron {evaluados})");
            throw new ScraperException(algunPrecioPresente ? TipoError.NoEncontrado : TipoError.FormatoCambiado,
                algunPrecioPresente ? "el juego existe en Nintendo eShop pero sin precio (no comprable o no lanzado)"
                                    : "los resultados de Nintendo no traen los campos 'msrp'/'salePrice'");
        }

        // Preferir la versión de Switch (no la de Switch 2) y, entre ediciones, la más barata.
        var elegido = candidatos.OrderBy(c => c.EsSwitch2).ThenBy(c => c.Regular).ThenBy(c => c.Actual).First();
        decimal reg = Math.Max(elegido.Regular, elegido.Actual);
        return ResultadoTienda.Exito(Tienda, elegido.Actual, reg, Catalogos.Digital, "Switch",
            elegido.Enlace ?? BaseSitio + "/us/search/#q=" + Uri.EscapeDataString(juego.NombreParaBuscar), elegido.Imagen);
    }

    private static string? Enlace(JsonElement h)
    {
        string? u = Texto(h, "url");
        if (string.IsNullOrWhiteSpace(u)) return null;
        if (u.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return u;
        return BaseSitio + (u.StartsWith('/') ? "" : "/") + u;
    }

    private static string? Imagen(JsonElement h)
    {
        foreach (var campo in new[] { "productImage", "productImageSquare", "horizontalHeaderImage", "heroBannerImage" })
        {
            string? u = Texto(h, campo);
            if (string.IsNullOrWhiteSpace(u)) continue;
            if (u.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return u;
            return BaseSitio + (u.StartsWith('/') ? "" : "/") + u;
        }
        return null;
    }

    private static string? Texto(JsonElement e, string prop) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>El campo platform puede ser texto o lista de textos: se unifica en un solo texto.</summary>
    private static string TextoOLista(JsonElement e, string prop)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(prop, out var v)) return "";
        if (v.ValueKind == JsonValueKind.String) return v.GetString() ?? "";
        if (v.ValueKind == JsonValueKind.Array)
            return string.Join(", ", v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()));
        return "";
    }

    /// <summary>Número como número o como texto ("59.99"); null si falta o no es un número.</summary>
    private static decimal? Numero(JsonElement e, string prop)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(prop, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d)) return d;
        if (v.ValueKind == JsonValueKind.String &&
            decimal.TryParse(v.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var s)) return s;
        return null;
    }
}
