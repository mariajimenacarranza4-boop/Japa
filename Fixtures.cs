using System.Net;
using System.Text;

namespace ScraperJuegos.Pruebas;

/// <summary>HttpMessageHandler falso: responde con una función y cuenta las solicitudes (seguro entre hilos).</summary>
public sealed class ManejadorFalso : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, int, HttpResponseMessage> _resp;
    private int _total;
    public readonly List<string> Urls = new();
    public int Total => _total;

    public ManejadorFalso(Func<HttpRequestMessage, int, HttpResponseMessage> resp) => _resp = resp;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        int n = Interlocked.Increment(ref _total);
        lock (Urls) Urls.Add(request.Method + " " + request.RequestUri);
        return Task.FromResult(_resp(request, n));
    }

    public static HttpResponseMessage Texto(HttpStatusCode c, string cuerpo, string tipo = "text/plain") =>
        new(c) { Content = new StringContent(cuerpo, Encoding.UTF8, tipo) };
    public static HttpResponseMessage Json(string cuerpo) => Texto(HttpStatusCode.OK, cuerpo, "application/json");
    public static HttpResponseMessage Html(string cuerpo) => Texto(HttpStatusCode.OK, cuerpo, "text/html");
}

/// <summary>Respuestas de ejemplo con la forma real de cada sitio (para probar sin internet).</summary>
public static class Fixtures
{
    public const string RobotsAbierto = "User-agent: *\nDisallow: /admin/\n";

    public static string SteamBusqueda(string nombre, long initial, long final, bool windows = true, long id = 1245620) => $$$$"""
    {"total":2,"items":[
      {"type":"app","name":"{{{{nombre}}}} Soundtrack","id":999,"price":{"currency":"USD","initial":999,"final":999},"platforms":{"windows":true,"mac":false,"linux":false}},
      {"type":"app","name":"{{{{nombre}}}}","id":{{{{id}}}},"price":{"currency":"USD","initial":{{{{initial}}}},"final":{{{{final}}}}},"platforms":{"windows":{{{{windows.ToString().ToLower()}}}},"mac":false,"linux":false}}
    ]}
    """;

    public static string MetacriticHtml(string nombre, int score, string plataforma, string? fecha = null) => $$$$"""
    <html><head><script type="application/ld+json">{"@context":"http://schema.org","@type":"VideoGame","name":"{{{{nombre}}}}",
      "gamePlatform":["{{{{plataforma}}}}"],{{{{(fecha == null ? "" : "\"datePublished\":\"" + fecha + "\",")}}}}"aggregateRating":{"@type":"AggregateRating","ratingValue":"{{{{score}}}}","bestRating":"100"}}</script></head><body></body></html>
    """;

    // ---- CheapShark /api/1.0/deals (Epic, storeID 25): forma real simplificada; los precios llegan como TEXTO ----
    public static string CheapSharkDeals(params (string titulo, string venta, string normal, string storeId)[] ofertas) =>
        "[" + string.Join(",", ofertas.Select((o, i) =>
            "{\"internalName\":\"X\",\"title\":\"" + o.titulo + "\",\"dealID\":\"abc" + i + "%3D\",\"storeID\":\"" + o.storeId +
            "\",\"gameID\":\"1\",\"salePrice\":\"" + o.venta + "\",\"normalPrice\":\"" + o.normal +
            "\",\"isOnSale\":\"1\",\"savings\":\"40.006668\",\"thumb\":\"https://img/cs" + i + ".jpg\"}")) + "]";

    // ---- Epic Games Store (GraphQL), Nintendo eShop (Algolia) y Eneba (JSON-LD): forma real simplificada ----

    public static string EpicRespuesta(string titulo, long descuento, long original, string moneda = "USD", string categoria = "games/edition/base") =>
        "{\"data\":{\"Catalog\":{\"searchStore\":{\"elements\":[" +
        "{\"title\":\"" + titulo + " Soundtrack\",\"id\":\"x1\",\"productSlug\":null,\"urlSlug\":\"sound\",\"keyImages\":[],\"categories\":[{\"path\":\"addons\"}],\"offerMappings\":[]," +
        "\"price\":{\"totalPrice\":{\"discountPrice\":999,\"originalPrice\":999,\"currencyCode\":\"USD\",\"currencyInfo\":{\"decimals\":2}}}}," +
        "{\"title\":\"" + titulo + "\",\"id\":\"x2\",\"productSlug\":\"elden-ring/home\",\"urlSlug\":\"elden\",\"keyImages\":[{\"type\":\"Thumbnail\",\"url\":\"https://img/epic.png\"}]," +
        "\"categories\":[{\"path\":\"" + categoria + "\"}],\"offerMappings\":[{\"pageSlug\":\"elden-ring\",\"pageType\":\"productHome\"}]," +
        "\"price\":{\"totalPrice\":{\"discountPrice\":" + descuento + ",\"originalPrice\":" + original + ",\"currencyCode\":\"" + moneda + "\",\"currencyInfo\":{\"decimals\":2}}}}" +
        "]}}}}";

    /// <param name="venta">"null" o un número, tal como va en el JSON. Forma real de Nintendo: price { regPrice, salePrice, finalPrice }.</param>
    public static string NintendoRespuesta(string titulo, string plataforma, string msrp, string venta) =>
        "{\"hits\":[" +
        "{\"title\":\"" + titulo + " Expansion Pass\",\"nsuid\":\"1\",\"url\":\"/store/products/exp/\",\"platform\":\"Nintendo Switch\",\"price\":{\"regPrice\":19.99,\"salePrice\":null,\"finalPrice\":19.99}}," +
        "{\"title\":\"" + titulo + "\",\"nsuid\":\"70010000001130\",\"url\":\"/store/products/juego-switch/\",\"platform\":\"" + plataforma + "\"," +
        "\"price\":{\"regPrice\":" + msrp + ",\"salePrice\":" + venta + ",\"finalPrice\":" + (venta == "null" ? msrp : venta) + "},\"productImage\":\"/content/dam/noa/img.png\"}" +
        "],\"nbHits\":2}";

    public static string EnebaHtml(string nombre, string precio, string moneda) =>
        "<html><head><script type=\"application/ld+json\">{\"@context\":\"https://schema.org\",\"@type\":\"ItemList\",\"itemListElement\":[" +
        "{\"@type\":\"Product\",\"name\":\"" + nombre + "\",\"url\":\"/elden-ring-steam-key-global\",\"image\":\"https://img/eneba.jpg\"," +
        "\"offers\":{\"@type\":\"Offer\",\"price\":\"" + precio + "\",\"priceCurrency\":\"" + moneda + "\"}}," +
        "{\"@type\":\"Product\",\"name\":\"Elden Ring (PC) Steam Key EUROPE\",\"offers\":{\"@type\":\"Offer\",\"price\":\"10.00\",\"priceCurrency\":\"" + moneda + "\"}}" +
        "]}</script></head><body></body></html>";

    /// <summary>
    /// Forma real de la búsqueda de Eneba (2026): los resultados vienen en &lt;script id="__ALGOLIA_STATE__"&gt;, con
    /// lowestPrice/msrp en la unidad menor (4676 = 46.76), productRegions y productType (game|dlc).
    /// </summary>
    public static string EnebaAlgoliaHtml(params (string nombre, string tipo, string drm, string[] regiones, long usd, long? msrp, string slug)[] hits)
    {
        var lista = hits.Select(h => new Dictionary<string, object?>
        {
            ["productType"] = h.tipo,
            ["productRegions"] = h.regiones,
            ["translations"] = new { en = new { name = h.nombre } },
            ["drmName"] = h.drm,
            ["slug"] = h.slug,
            ["stockAvailable"] = true,
            ["lowestPrice"] = new { EUR = h.usd - 500, USD = h.usd },
            ["msrp"] = h.msrp is null ? null : new { USD = h.msrp.Value },
            ["images"] = new { cover300 = new { src = "https://img/eneba-300.jpg" } },
        }).ToList();
        var estado = new
        {
            responses = new Dictionary<string, object>
            {
                ["q"] = new { results = new[] { new { hits = lista, nbHits = lista.Count } } }
            }
        };
        return "<html><body><script id=\"__ALGOLIA_STATE__\" type=\"application/json\">"
             + System.Text.Json.JsonSerializer.Serialize(estado) + "</script></body></html>";
    }
}
