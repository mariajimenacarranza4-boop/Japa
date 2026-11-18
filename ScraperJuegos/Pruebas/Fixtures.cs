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

    // ---- Epic Games Store vía CheapShark (API pública), Nintendo eShop (Algolia) y Eneba (JSON-LD): forma real simplificada ----

    /// <summary>GET /api/1.0/stores: lista de tiendas; los ids y banderas vienen como texto/número igual que en la API real.</summary>
    public static string CheapSharkTiendas(string epicId = "25", bool epicActiva = true) =>
        "[{\"storeID\":\"1\",\"storeName\":\"Steam\",\"isActive\":1,\"images\":{\"banner\":\"/img/stores/banners/0.png\"}}," +
        "{\"storeID\":\"" + epicId + "\",\"storeName\":\"Epic Games Store\",\"isActive\":" + (epicActiva ? "1" : "0") + ",\"images\":{}}," +
        "{\"storeID\":\"7\",\"storeName\":\"GOG\",\"isActive\":1,\"images\":{}}]";

    /// <summary>Una oferta de GET /api/1.0/deals (precios como texto decimal; dealID ya codificado para URL).</summary>
    public static string CheapSharkOferta(string titulo, string venta, string normal, string storeId = "25", string dealId = "AbC%2Bxyz%3D",
                                          string steamAppId = "1245620", string thumb = "https://img/epic.png") =>
        "{\"internalName\":\"X\",\"title\":\"" + titulo + "\",\"metacriticLink\":\"/game/pc/x\",\"dealID\":\"" + dealId + "\",\"storeID\":\"" + storeId + "\"," +
        "\"gameID\":\"1\",\"salePrice\":\"" + venta + "\",\"normalPrice\":\"" + normal + "\",\"isOnSale\":\"1\",\"savings\":\"40.0\"," +
        "\"steamAppID\":\"" + steamAppId + "\",\"thumb\":\"" + thumb + "\"}";

    // ---- IsThereAnyDeal (respaldo de Epic): forma real simplificada de https://docs.isthereanydeal.com ----
    public const string ItadGameId = "018d937f-07fc-72ed-8517-d8e24cb1eb22";

    public static string ItadTiendas(bool epic = true) =>
        "[{\"id\":61,\"title\":\"Steam\",\"deals\":10,\"games\":20,\"update\":null}" +
        (epic ? ",{\"id\":16,\"title\":\"Epic Game Store\",\"deals\":5,\"games\":9,\"update\":null}" : "") + "]";

    public static string ItadLookup(bool encontrado = true, string slug = "elden-ring", string titulo = "ELDEN RING") =>
        encontrado ? "{\"found\":true,\"game\":{\"id\":\"" + ItadGameId + "\",\"slug\":\"" + slug + "\",\"title\":\"" + titulo + "\",\"type\":\"game\",\"mature\":false,\"assets\":{}}}"
                   : "{\"found\":false}";

    public static string ItadBusqueda(params (string titulo, string tipo)[] items) =>
        "[" + string.Join(",", items.Select((x, i) => "{\"id\":\"" + (i == 0 ? ItadGameId : "0000000" + i) + "\",\"slug\":\"s" + i + "\",\"title\":\"" + x.titulo + "\",\"type\":\"" + x.tipo + "\",\"mature\":false,\"assets\":{}}")) + "]";

    public static string ItadPrecios(string actual, string regular, string shopId = "16", string moneda = "USD", string? url = "https://isthereanydeal.com/redirect/epic-elden-ring") =>
        "[{\"id\":\"" + ItadGameId + "\",\"historyLow\":{},\"deals\":[{\"shop\":{\"id\":" + shopId + ",\"name\":\"Epic Game Store\"}," +
        "\"price\":{\"amount\":" + actual + ",\"amountInt\":0,\"currency\":\"" + moneda + "\"}," +
        "\"regular\":{\"amount\":" + regular + ",\"amountInt\":0,\"currency\":\"" + moneda + "\"},\"cut\":40,\"voucher\":null,\"flag\":null,\"drm\":[],\"platforms\":[]," +
        "\"timestamp\":\"2026-11-14T10:00:00+01:00\",\"expiry\":null" + (url != null ? ",\"url\":\"" + url + "\"" : "") + "}]}]";

    public static string CheapSharkOfertas(params string[] ofertas) => "[" + string.Join(",", ofertas) + "]";

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
