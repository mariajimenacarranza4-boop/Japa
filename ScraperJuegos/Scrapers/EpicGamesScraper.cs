using System.Text;
using System.Text.Json;

namespace ScraperJuegos;

/// <summary>
/// [NO REGISTRADO] Acceso DIRECTO a Epic: store.epicgames.com responde 403 a las consultas automáticas (11-nov-2026) y el
/// equipo decidió no evadirlo. Se conserva como evidencia y por si Epic vuelve a permitirlo; la tienda activa es EpicCheapSharkScraper.
/// Tienda 2: Epic Games Store (solo PC, formato digital), región US (USD).
/// Usa el endpoint GraphQL que usa la propia tienda (POST https://store.epicgames.com/graphql, consulta searchStoreQuery),
/// sin HTML ni cuenta. Los precios llegan como ENTEROS en la unidad menor (1999 = 19.99) junto con currencyInfo.decimals.
/// Una sola solicitud por juego. Sin estado compartido.
/// Si la respuesta trae "errors" o cambia de forma -> formato_cambiado; si no hay coincidencia segura -> no_encontrado.
/// </summary>
public sealed class EpicGamesScraper : ITiendaScraper
{
    public string Tienda => "Epic Games Store";
    public string Dominio => "store.epicgames.com";
    private readonly ClienteHttp _http;

    private const string UrlGraphQl = "https://store.epicgames.com/graphql";

    // Consulta mínima: solo los campos que se usan (si Epic quita uno, la respuesta trae "errors" y se reporta formato_cambiado).
    private const string Consulta =
        "query searchStoreQuery($country: String!, $keywords: String, $locale: String, $count: Int) { " +
        "Catalog { searchStore(country: $country, keywords: $keywords, locale: $locale, count: $count) { " +
        "elements { title id productSlug urlSlug keyImages { type url } categories { path } " +
        "offerMappings { pageSlug pageType } " +
        "price(country: $country) { totalPrice { discountPrice originalPrice currencyCode currencyInfo { decimals } } } } " +
        "paging { count total } } } }";

    public EpicGamesScraper(ClienteHttp http) => _http = http;

    public bool SoportaPlataforma(string plataforma) => plataforma == "PC";

    public Task<ResultadoTienda> ConsultarAsync(Juego juego, CancellationToken ct = default) =>
        Seguro.EjecutarAsync(() => ConsultarInternoAsync(juego, ct),
            (tipo, det) => ResultadoTienda.Fallo(Tienda, tipo, det), _http.Log, juego, Tienda, ct);

    private sealed record OfertaEpic(string Titulo, decimal Actual, decimal Regular, string? Moneda, string? Slug, string? Imagen);

    private async Task<ResultadoTienda> ConsultarInternoAsync(Juego juego, CancellationToken ct)
    {
        if (!SoportaPlataforma(juego.Plataforma))
            throw new ScraperException(TipoError.NoEncontrado, $"Epic Games Store solo vende para PC (juego en {juego.Plataforma})");

        var ctx = new ContextoLog(juego.Nombre, juego.Plataforma, Tienda);
        string cuerpo = JsonSerializer.Serialize(new
        {
            query = Consulta,
            variables = new { country = "US", locale = "en-US", keywords = juego.NombreParaBuscar, count = 20 }
        });

        var resp = await _http.EnviarAsync(() =>
        {
            var r = new HttpRequestMessage(HttpMethod.Post, UrlGraphQl)
            { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };
            r.Headers.TryAddWithoutValidation("Referer", "https://store.epicgames.com/");
            r.Headers.TryAddWithoutValidation("Origin", "https://store.epicgames.com");
            return r;
        }, new Uri(UrlGraphQl), ctx, ct).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(resp.Cuerpo);
        var raiz = doc.RootElement;

        if (!TryRuta(raiz, out var elementos, "data", "Catalog", "searchStore", "elements") || elementos.ValueKind != JsonValueKind.Array)
        {
            string motivo = raiz.ValueKind == JsonValueKind.Object && raiz.TryGetProperty("errors", out var errs)
                            && errs.ValueKind == JsonValueKind.Array && errs.GetArrayLength() > 0
                ? "GraphQL devolvió errores: " + (Texto(errs[0], "message") ?? "sin mensaje")
                : "la respuesta no trae data.Catalog.searchStore.elements";
            throw new ScraperException(TipoError.FormatoCambiado, motivo);
        }

        int evaluados = 0;
        var candidatas = new List<OfertaEpic>();
        bool algunaConPrecioNoUsd = false;
        foreach (var el in elementos.EnumerateArray())
        {
            evaluados++;
            string? titulo = Texto(el, "title");
            if (!NormalizadorNombres.CoincideConJuego(juego, titulo)) continue;
            if (EsExtra(el)) continue; // DLC, banda sonora, etc.

            if (!TryRuta(el, out var total, "price", "totalPrice") || total.ValueKind != JsonValueKind.Object) continue;
            long? desc = Entero(total, "discountPrice"), orig = Entero(total, "originalPrice");
            if (desc is null || orig is null) continue;
            int decimales = TryRuta(total, out var ci, "currencyInfo") && ci.ValueKind == JsonValueKind.Object
                            && Entero(ci, "decimals") is { } d && d is >= 0 and <= 4 ? (int)d : 2;
            string? moneda = Texto(total, "currencyCode");
            if (moneda != null && !moneda.Equals("USD", StringComparison.OrdinalIgnoreCase)) { algunaConPrecioNoUsd = true; continue; }

            decimal divisor = (decimal)Math.Pow(10, decimales);
            decimal actual = desc.Value / divisor, regular = Math.Max(orig.Value, desc.Value) / divisor;
            candidatas.Add(new OfertaEpic(titulo!, actual, regular, moneda, Slug(el), Imagen(el)));
        }

        if (candidatas.Count == 0)
        {
            if (algunaConPrecioNoUsd)
                throw new ScraperException(TipoError.FormatoCambiado, "Epic devolvió precios en otra moneda que USD; no se mezclan monedas");
            throw new ScraperException(TipoError.NoEncontrado,
                $"ningún resultado de Epic Games Store coincide con '{juego.NombreParaBuscar}' (se evaluaron {evaluados})");
        }

        // Varias ediciones (estándar/deluxe): la base es la de menor precio regular (y de ahí la más barata hoy).
        var elegida = candidatas.OrderBy(c => c.Regular).ThenBy(c => c.Actual).First();
        if (elegida.Regular <= 0)
            throw new ScraperException(TipoError.NoEncontrado, "el producto es gratuito o no muestra precio en Epic Games Store");

        string enlace = elegida.Slug != null
            ? "https://store.epicgames.com/en-US/p/" + Uri.EscapeDataString(elegida.Slug)
            : "https://store.epicgames.com/en-US/browse?q=" + Uri.EscapeDataString(juego.NombreParaBuscar) + "&sortBy=relevancy";
        return ResultadoTienda.Exito(Tienda, elegida.Actual, elegida.Regular, Catalogos.Digital, "PC", enlace, elegida.Imagen);
    }

    /// <summary>Descarta contenido que no es el juego (complementos, bandas sonoras...). Si no hay categorías, se acepta.</summary>
    private static bool EsExtra(JsonElement el)
    {
        if (!el.TryGetProperty("categories", out var cats) || cats.ValueKind != JsonValueKind.Array) return false;
        foreach (var c in cats.EnumerateArray())
        {
            string? p = Texto(c, "path");
            if (p != null && (p.StartsWith("addons", StringComparison.OrdinalIgnoreCase)
                              || p.StartsWith("digitalextras", StringComparison.OrdinalIgnoreCase))) return true;
        }
        return false;
    }

    private static string? Slug(JsonElement el)
    {
        if (el.TryGetProperty("offerMappings", out var om) && om.ValueKind == JsonValueKind.Array)
            foreach (var m in om.EnumerateArray())
                if (Texto(m, "pageSlug") is { Length: > 0 } s) return LimpiarSlug(s);
        if (Texto(el, "productSlug") is { Length: > 0 } ps) return LimpiarSlug(ps);
        if (Texto(el, "urlSlug") is { Length: > 0 } us && !us.Contains(' ')) return LimpiarSlug(us);
        return null;
    }

    private static string LimpiarSlug(string s) => s.EndsWith("/home", StringComparison.Ordinal) ? s[..^5] : s;

    private static string? Imagen(JsonElement el)
    {
        if (!el.TryGetProperty("keyImages", out var ki) || ki.ValueKind != JsonValueKind.Array) return null;
        string? primera = null;
        foreach (var k in ki.EnumerateArray())
        {
            string? url = Texto(k, "url");
            if (url is null) continue;
            primera ??= url;
            string? tipo = Texto(k, "type");
            if (tipo is "OfferImageWide" or "DieselStoreFrontWide" or "Thumbnail") return url;
        }
        return primera;
    }

    private static bool TryRuta(JsonElement e, out JsonElement resultado, params string[] ruta)
    {
        resultado = e;
        foreach (var p in ruta)
        {
            if (resultado.ValueKind != JsonValueKind.Object || !resultado.TryGetProperty(p, out var sig)) return false;
            resultado = sig;
        }
        return true;
    }

    private static string? Texto(JsonElement e, string prop) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static long? Entero(JsonElement e, string prop) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : null;
}
