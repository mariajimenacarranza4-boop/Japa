using System.Text.Json;

namespace ScraperJuegos;

/// <summary>
/// Tienda 1: Steam (solo PC, formato digital). Usa los endpoints JSON que usa la propia tienda
/// (store.steampowered.com, NO api.steampowered.com), sin HTML ni cuenta, en dólares (cc=us):
///   - Búsqueda: /api/storesearch/?term=...&amp;cc=us&amp;l=english   (1 solicitud por juego)
///   - Detalle (solo si el REPO trae steam_appid): /api/appdetails?appids=ID&amp;cc=us&amp;l=english
/// Sin estado compartido: cada llamada es independiente.
/// </summary>
public sealed class SteamScraper : ITiendaScraper
{
    public string Tienda => "Steam";
    public string Dominio => "store.steampowered.com";
    private readonly ClienteHttp _http;

    public SteamScraper(ClienteHttp http) => _http = http;

    public bool SoportaPlataforma(string plataforma) => plataforma == "PC";

    public Task<ResultadoTienda> ConsultarAsync(Juego juego, CancellationToken ct = default) =>
        Seguro.EjecutarAsync(() => ConsultarInternoAsync(juego, ct),
            (tipo, det) => ResultadoTienda.Fallo(Tienda, tipo, det), _http.Log, juego, Tienda, ct);

    private async Task<ResultadoTienda> ConsultarInternoAsync(Juego juego, CancellationToken ct)
    {
        if (!SoportaPlataforma(juego.Plataforma))
            throw new ScraperException(TipoError.NoEncontrado, $"Steam solo vende para PC (juego en {juego.Plataforma})");

        var ctx = new ContextoLog(juego.Nombre, juego.Plataforma, Tienda);
        return !string.IsNullOrWhiteSpace(juego.SteamAppId)
            ? await PorAppIdAsync(juego, juego.SteamAppId!.Trim(), ctx, ct).ConfigureAwait(false)
            : await PorBusquedaAsync(juego, ctx, ct).ConfigureAwait(false);
    }

    // ---- Ruta 1: búsqueda por nombre ----
    private async Task<ResultadoTienda> PorBusquedaAsync(Juego juego, ContextoLog ctx, CancellationToken ct)
    {
        string url = "https://store.steampowered.com/api/storesearch/?term=" +
                     Uri.EscapeDataString(juego.NombreParaBuscar) + "&cc=us&l=english";
        var resp = await _http.ObtenerAsync(url, ctx, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(resp.Cuerpo);

        if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            throw new ScraperException(TipoError.FormatoCambiado, "la búsqueda de Steam no trae el arreglo 'items'");

        string? sinPrecio = null;   // coincidencia que existe pero no se vende: se sigue buscando otra (ej. versión vieja retirada)
        foreach (var item in items.EnumerateArray())
        {
            string? nombre = Texto(item, "name");
            if (!NormalizadorNombres.CoincideConJuego(juego, nombre)) continue;
            if (item.TryGetProperty("platforms", out var pl) && pl.ValueKind == JsonValueKind.Object
                && pl.TryGetProperty("windows", out var win) && win.ValueKind == JsonValueKind.False) continue;

            string? id = item.TryGetProperty("id", out var idEl) ? idEl.ToString() : null;
            if (string.IsNullOrEmpty(id))
                throw new ScraperException(TipoError.FormatoCambiado, "un resultado de Steam no trae 'id'");

            if (!item.TryGetProperty("price", out var precio) || precio.ValueKind != JsonValueKind.Object)
            {
                sinPrecio ??= nombre;
                continue;
            }

            return Construir(id, Entero(precio, "initial"), Entero(precio, "final"), null);
        }
        if (sinPrecio != null)
            throw new ScraperException(TipoError.NoEncontrado,
                $"'{sinPrecio}' existe en Steam pero sin precio (gratis, retirado de la venta o no disponible en US)");
        throw new ScraperException(TipoError.NoEncontrado,
            $"ningún resultado de Steam coincide con '{juego.NombreParaBuscar}' (se evaluaron {items.GetArrayLength()})");
    }

    // ---- Ruta 2: el REPO ya trae el appid ----
    private async Task<ResultadoTienda> PorAppIdAsync(Juego juego, string appId, ContextoLog ctx, CancellationToken ct)
    {
        string url = $"https://store.steampowered.com/api/appdetails?appids={Uri.EscapeDataString(appId)}&cc=us&l=english";
        var resp = await _http.ObtenerAsync(url, ctx, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(resp.Cuerpo);

        if (!doc.RootElement.TryGetProperty(appId, out var nodo) || !nodo.TryGetProperty("success", out var ok))
            throw new ScraperException(TipoError.FormatoCambiado, "appdetails no trae el appid pedido o 'success'");
        if (ok.ValueKind != JsonValueKind.True)
            throw new ScraperException(TipoError.NoEncontrado, $"Steam no tiene el appid {appId} en la región US");
        if (!nodo.TryGetProperty("data", out var data))
            throw new ScraperException(TipoError.FormatoCambiado, "appdetails con success=true pero sin 'data'");

        if (data.TryGetProperty("type", out var tipo) && tipo.GetString() is { } t && t != "game")
            throw new ScraperException(TipoError.NoEncontrado, $"el appid {appId} es de tipo '{t}', no 'game'");
        if (!NormalizadorNombres.CoincideConJuego(juego, Texto(data, "name")))
            throw new ScraperException(TipoError.NoEncontrado,
                $"el appid {appId} es '{Texto(data, "name")}', no '{juego.Nombre}' (revise steam_appid en el REPO)");
        if (!data.TryGetProperty("price_overview", out var po) || po.ValueKind != JsonValueKind.Object)
            throw new ScraperException(TipoError.NoEncontrado, "el juego no tiene precio en Steam (gratis, no lanzado o retirado)");

        return Construir(appId, Entero(po, "initial"), Entero(po, "final"), Texto(data, "header_image"));
    }

    private ResultadoTienda Construir(string id, long? initialCent, long? finalCent, string? imagen)
    {
        if (initialCent is null || finalCent is null || finalCent <= 0)
            throw new ScraperException(TipoError.FormatoCambiado, "el precio de Steam no trae 'initial'/'final' numéricos");
        decimal actual = finalCent.Value / 100m, regular = Math.Max(initialCent.Value, finalCent.Value) / 100m;
        imagen ??= $"https://cdn.akamai.steamstatic.com/steam/apps/{id}/header.jpg";
        return ResultadoTienda.Exito(Tienda, actual, regular, Catalogos.Digital, "PC",
            $"https://store.steampowered.com/app/{id}/", imagen);
    }

    private static string? Texto(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static long? Entero(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : null;
}
