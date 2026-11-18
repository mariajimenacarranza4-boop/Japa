using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ScraperJuegos;

/// <summary>
/// Respaldo de Epic Games Store: la API OFICIAL de IsThereAnyDeal (https://docs.isthereanydeal.com), que sí cubre los precios de Epic.
/// Solo se usa si CheapShark falla (bloqueo, formato, juego no encontrado) y hay una clave configurada: variable ITAD_API_KEY
/// (clave gratuita: crear una "app" en https://isthereanydeal.com/apps/). Sin clave, este respaldo no hace ninguna solicitud.
///
/// Flujo (máximo 3 solicitudes por juego; la lista de tiendas se pide una vez por corrida):
///   1. GET  /service/shops/v1?country=US       -> id de la tienda Epic, buscada por NOMBRE (se puede fijar con ITAD_EPIC_SHOP_ID).
///   2. GET  /games/lookup/v1?appid=N | ?title=T -> id del juego en ITAD (por steam_appid si el REPO lo trae; luego por título exacto).
///      GET  /games/search/v1?title=T            -> solo si el título exacto no se encontró; se acepta únicamente un resultado que
///                                                  coincida con el juego (misma regla de nombres que el resto del proyecto).
///   3. POST /games/prices/v3?country=US&amp;shops=ID, cuerpo ["id-del-juego"] -> precio actual y regular en Epic (USD).
///
/// Seguridad y cortesía: la clave viaja en la cabecera ITAD-API-Key (nunca en la URL, para que no quede en logs); respeta robots.txt
/// y la pausa de 1 s por dominio como todo el proyecto; límite de ITAD = 1000 solicitudes cada 5 min (aquí ~300); si responde
/// 401/403/429 persistente se deja de insistir unos minutos (disyuntor).
/// </summary>
internal sealed class ItadEpicFuente
{
    internal const string UrlBase = "https://api.isthereanydeal.com";
    private static readonly TimeSpan DuracionDisyuntor = TimeSpan.FromMinutes(5);

    private readonly ClienteHttp _http;
    private readonly SemaphoreSlim _candado = new(1, 1);
    private string? _shopId;
    private DateTime _bloqueadoHastaUtc;
    private string _detalleBloqueo = "";

    public ItadEpicFuente(ClienteHttp http)
    {
        _http = http;
        string? fijo = http.Config.ItadEpicShopId?.Trim();
        if (!string.IsNullOrEmpty(fijo) && fijo.All(char.IsDigit)) _shopId = fijo;
    }

    public bool Configurada => !string.IsNullOrWhiteSpace(_http.Config.ItadApiKey);

    public async Task<ResultadoTienda> ConsultarAsync(string tienda, Juego juego, ContextoLog ctx, CancellationToken ct)
    {
        if (!Configurada) throw new ScraperException(TipoError.Bloqueo, "respaldo ITAD no configurado (defina la variable ITAD_API_KEY)");
        if (DateTime.UtcNow < _bloqueadoHastaUtc)
            throw new ScraperException(TipoError.Bloqueo, $"ITAD bloqueó antes en esta corrida; no se insiste ({_detalleBloqueo})");
        try
        {
            string shopId = await ObtenerShopIdAsync(ctx, ct).ConfigureAwait(false);
            var (gameId, slug) = await ObtenerJuegoAsync(juego, ctx, ct).ConfigureAwait(false);

            string url = $"{UrlBase}/games/prices/v3?country=US&shops={shopId}";
            using var doc = await PedirJsonAsync(() => Post(url, "[\"" + gameId + "\"]"), url, ctx, ct).ConfigureAwait(false);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                throw new ScraperException(TipoError.FormatoCambiado, "ITAD /games/prices no devolvió una lista");

            decimal? mejorActual = null, mejorRegular = null; string? enlace = null;
            foreach (var juegoEl in doc.RootElement.EnumerateArray())
            {
                if (Texto(juegoEl, "id") != gameId || !juegoEl.TryGetProperty("deals", out var deals) || deals.ValueKind != JsonValueKind.Array) continue;
                foreach (var d in deals.EnumerateArray())
                {
                    if (!d.TryGetProperty("shop", out var shop) || Texto(shop, "id") != shopId) continue;
                    decimal? actual = Monto(d, "price", out string? mon1), regular = Monto(d, "regular", out string? mon2);
                    if (actual is null || regular is null) continue;
                    if (mon1 != "USD" || mon2 != "USD")
                        throw new ScraperException(TipoError.FormatoCambiado, $"ITAD devolvió moneda {mon1 ?? "?"} y se esperaba USD");
                    if (mejorActual is null || actual < mejorActual)
                    {
                        mejorActual = actual; mejorRegular = Math.Max(regular.Value, actual.Value);
                        string? u = Texto(d, "url");
                        enlace = u is { Length: > 0 } && u.StartsWith("https://") ? u : null;
                    }
                }
            }
            if (mejorActual is null)
                throw new ScraperException(TipoError.NoEncontrado, "ITAD conoce el juego pero no tiene precio en Epic Games Store");
            if (mejorRegular <= 0)
                throw new ScraperException(TipoError.NoEncontrado, "el producto es gratuito o no muestra precio en Epic Games Store");

            enlace ??= $"https://isthereanydeal.com/game/{Uri.EscapeDataString(slug)}/info/";
            return ResultadoTienda.Exito(tienda, mejorActual.Value, mejorRegular!.Value, Catalogos.Digital, "PC", enlace, null);
        }
        catch (ScraperException ex) when (ex.Tipo == TipoError.Bloqueo)
        {
            _bloqueadoHastaUtc = DateTime.UtcNow + DuracionDisyuntor;
            _detalleBloqueo = ex.Message;
            throw;
        }
    }

    private async Task<string> ObtenerShopIdAsync(ContextoLog ctx, CancellationToken ct)
    {
        if (_shopId != null) return _shopId;
        await _candado.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_shopId != null) return _shopId;
            string url = $"{UrlBase}/service/shops/v1?country=US";
            using var doc = await PedirJsonAsync(() => Get(url), url, ctx, ct).ConfigureAwait(false);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                throw new ScraperException(TipoError.FormatoCambiado, "ITAD /service/shops no devolvió una lista de tiendas");
            foreach (var t in doc.RootElement.EnumerateArray())
            {
                string? nombre = Texto(t, "title"), id = Texto(t, "id");
                if (nombre != null && nombre.Contains("epic", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(id) && id.All(char.IsDigit))
                { _shopId = id; return id; }
            }
            throw new ScraperException(TipoError.FormatoCambiado, "ITAD no lista a Epic Games Store entre sus tiendas");
        }
        finally { _candado.Release(); }
    }

    private async Task<(string id, string slug)> ObtenerJuegoAsync(Juego juego, ContextoLog ctx, CancellationToken ct)
    {
        // 1) por steam_appid (identificador exacto)
        if (!string.IsNullOrWhiteSpace(juego.SteamAppId) && juego.SteamAppId!.Trim().All(char.IsDigit))
        {
            var r = await LookupAsync($"{UrlBase}/games/lookup/v1?appid={juego.SteamAppId.Trim()}", ctx, ct).ConfigureAwait(false);
            if (r != null) return r.Value;
        }
        // 2) por título exacto
        string titulo = Uri.EscapeDataString(juego.NombreParaBuscar);
        var porTitulo = await LookupAsync($"{UrlBase}/games/lookup/v1?title={titulo}", ctx, ct).ConfigureAwait(false);
        if (porTitulo != null) return porTitulo.Value;

        // 3) búsqueda: solo se acepta un resultado que coincida con el juego (no un DLC ni otro título parecido)
        string urlBusqueda = $"{UrlBase}/games/search/v1?title={titulo}&results=10";
        using var doc = await PedirJsonAsync(() => Get(urlBusqueda), urlBusqueda, ctx, ct).ConfigureAwait(false);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new ScraperException(TipoError.FormatoCambiado, "ITAD /games/search no devolvió una lista");
        foreach (var g in doc.RootElement.EnumerateArray())
        {
            string? tipo = Texto(g, "type");
            if (tipo is not (null or "game")) continue;
            if (!NormalizadorNombres.CoincideConJuego(juego, Texto(g, "title"))) continue;
            string? id = Texto(g, "id");
            if (!string.IsNullOrEmpty(id)) return (id, Texto(g, "slug") ?? id);
        }
        throw new ScraperException(TipoError.NoEncontrado, $"ITAD no tiene un juego que coincida con '{juego.NombreParaBuscar}'");
    }

    private async Task<(string id, string slug)?> LookupAsync(string url, ContextoLog ctx, CancellationToken ct)
    {
        using var doc = await PedirJsonAsync(() => Get(url), url, ctx, ct).ConfigureAwait(false);
        var raiz = doc.RootElement;
        if (raiz.ValueKind != JsonValueKind.Object) throw new ScraperException(TipoError.FormatoCambiado, "ITAD /games/lookup no devolvió un objeto");
        if (!raiz.TryGetProperty("found", out var f) || f.ValueKind != JsonValueKind.True) return null;
        if (!raiz.TryGetProperty("game", out var g) || Texto(g, "id") is not { Length: > 0 } id)
            throw new ScraperException(TipoError.FormatoCambiado, "ITAD /games/lookup dice found=true pero no trae 'game.id'");
        return (id, Texto(g, "slug") ?? id);
    }

    private HttpRequestMessage Get(string url) => Con(new HttpRequestMessage(HttpMethod.Get, url));

    private HttpRequestMessage Post(string url, string json) =>
        Con(new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(json, Encoding.UTF8, "application/json") });

    /// <summary>La clave va en cabecera: así no queda escrita en las URL que se muestran en los mensajes de error ni en fallos.log.</summary>
    private HttpRequestMessage Con(HttpRequestMessage r)
    {
        r.Headers.TryAddWithoutValidation("ITAD-API-Key", _http.Config.ItadApiKey!.Trim());
        r.Headers.TryAddWithoutValidation("Accept", "application/json");
        return r;
    }

    private async Task<JsonDocument> PedirJsonAsync(Func<HttpRequestMessage> fabrica, string url, ContextoLog ctx, CancellationToken ct)
    {
        RespuestaHttp resp;
        try { resp = await _http.EnviarAsync(fabrica, new Uri(url), ctx, ct).ConfigureAwait(false); }
        catch (ScraperException ex) when (ex.Tipo == TipoError.Bloqueo && ex.Message.Contains("acceso denegado"))
        {
            throw new ScraperException(TipoError.Bloqueo, "ITAD rechazó la solicitud (clave inválida o sin permiso): revise ITAD_API_KEY");
        }
        try { return JsonDocument.Parse(resp.Cuerpo); }
        catch (JsonException) { throw new ScraperException(TipoError.FormatoCambiado, "ITAD no devolvió JSON válido"); }
    }

    private static string? Texto(JsonElement e, string prop)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(prop, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null
        };
    }

    private static decimal? Monto(JsonElement deal, string prop, out string? moneda)
    {
        moneda = null;
        if (!deal.TryGetProperty(prop, out var p) || p.ValueKind != JsonValueKind.Object) return null;
        moneda = Texto(p, "currency");
        return Texto(p, "amount") is { } s && decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) && d >= 0 ? d : null;
    }
}
