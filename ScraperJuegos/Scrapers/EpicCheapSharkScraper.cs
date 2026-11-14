using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScraperJuegos;

/// <summary>
/// Tienda 2: Epic Games Store, obtenida a través de la API pública de CheapShark (www.cheapshark.com).
///
/// Por qué así: la tienda de Epic (store.epicgames.com) responde 403 a las consultas automáticas y el equipo decidió NO
/// evadir ese bloqueo (ver EpicGamesScraper y la sección 3.5). CheapShark es un comparador de precios de PC con una API
/// pública, gratuita y sin clave, que rastrea Epic (storeID = 25) y entrega precio de oferta, precio normal y enlace.
/// El dato es de Epic pero llega por un INTERMEDIARIO: puede tener algunas horas de atraso respecto a la tienda.
///
/// Solicitudes (GET, JSON, USD):
///   1) /api/1.0/deals?storeID=25&amp;title=NOMBRE&amp;exact=1     (coincidencia exacta del título; 1 solicitud en la mayoría de juegos)
///   2) solo si la 1 no encuentra el juego: lo mismo con exact=0 (búsqueda parcial) y se filtra con NormalizadorNombres.
/// El enlace es el redireccionador de CheapShark (https://www.cheapshark.com/redirect?dealID=...), que lleva a la página
/// del juego en Epic; es la forma de enlazar que usa el propio sitio.
/// Sin estado compartido. Errores: igual que las otras tiendas (bloqueo / timeout / formato_cambiado / no_encontrado).
/// </summary>
public sealed class EpicCheapSharkScraper : ITiendaScraper
{
    public string Tienda => "Epic Games Store";
    public string Dominio => "www.cheapshark.com";
    private readonly ClienteHttp _http;

    /// <summary>Id de Epic en CheapShark (lista en /api/1.0/stores). Se puede cambiar con CHEAPSHARK_EPIC_STORE_ID sin recompilar.</summary>
    public static string IdTiendaEpic => Environment.GetEnvironmentVariable("CHEAPSHARK_EPIC_STORE_ID") is { Length: > 0 } v ? v.Trim() : "25";

    private const string UrlBase = "https://www.cheapshark.com/api/1.0/deals";

    public EpicCheapSharkScraper(ClienteHttp http) => _http = http;

    public bool SoportaPlataforma(string plataforma) => plataforma == "PC";

    public Task<ResultadoTienda> ConsultarAsync(Juego juego, CancellationToken ct = default) =>
        Seguro.EjecutarAsync(() => ConsultarInternoAsync(juego, ct),
            (tipo, det) => ResultadoTienda.Fallo(Tienda, tipo, det), _http.Log, juego, Tienda, ct);

    private sealed record OfertaCs(string Titulo, decimal Actual, decimal Regular, string DealId, string? Imagen);

    private async Task<ResultadoTienda> ConsultarInternoAsync(Juego juego, CancellationToken ct)
    {
        if (!SoportaPlataforma(juego.Plataforma))
            throw new ScraperException(TipoError.NoEncontrado, $"Epic Games Store solo vende para PC (juego en {juego.Plataforma})");

        var ctx = new ContextoLog(juego.Nombre, juego.Plataforma, Tienda);
        var (candidatas, evaluados) = await BuscarAsync(juego, exacto: true, ctx, ct).ConfigureAwait(false);
        if (candidatas.Count == 0)
        {
            var (parciales, ev2) = await BuscarAsync(juego, exacto: false, ctx, ct).ConfigureAwait(false);
            candidatas = parciales;
            evaluados += ev2;
        }

        if (candidatas.Count == 0)
            throw new ScraperException(TipoError.NoEncontrado,
                $"CheapShark no tiene una oferta de Epic que coincida con '{juego.NombreParaBuscar}' (se evaluaron {evaluados})");

        // Varias ediciones (estándar/deluxe): la base es la de menor precio normal (y de ahí la más barata hoy). Igual que en las otras tiendas.
        var elegida = candidatas.OrderBy(c => c.Regular).ThenBy(c => c.Actual).First();
        if (elegida.Regular <= 0)
            throw new ScraperException(TipoError.NoEncontrado, "el juego es gratuito en Epic Games Store (no hay precio que comparar)");

        string enlace = "https://www.cheapshark.com/redirect?dealID=" + IdSeguro(elegida.DealId);
        return ResultadoTienda.Exito(Tienda, elegida.Actual, elegida.Regular, Catalogos.Digital, "PC", enlace, elegida.Imagen);
    }

    private async Task<(List<OfertaCs> candidatas, int evaluados)> BuscarAsync(Juego juego, bool exacto, ContextoLog ctx, CancellationToken ct)
    {
        string url = $"{UrlBase}?storeID={Uri.EscapeDataString(IdTiendaEpic)}&title={Uri.EscapeDataString(juego.NombreParaBuscar)}" +
                     $"&exact={(exacto ? 1 : 0)}&pageSize=60";
        var resp = await _http.ObtenerAsync(url, ctx, ct).ConfigureAwait(false);
        return Interpretar(juego, resp.Cuerpo);
    }

    private static (List<OfertaCs> candidatas, int evaluados) Interpretar(Juego juego, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var raiz = doc.RootElement;
        if (raiz.ValueKind != JsonValueKind.Array)
        {
            string motivo = raiz.ValueKind == JsonValueKind.Object && Texto(raiz, "error") is { } err
                ? "CheapShark devolvió un error: " + err
                : "la respuesta de CheapShark /deals no es una lista";
            throw new ScraperException(TipoError.FormatoCambiado, motivo);
        }

        var lista = new List<OfertaCs>();
        int evaluados = 0;
        foreach (var d in raiz.EnumerateArray())
        {
            evaluados++;
            if (d.ValueKind != JsonValueKind.Object) continue;
            // Defensa: aunque se pidió storeID=25, solo se aceptan ofertas de Epic.
            if (TextoONumero(d, "storeID") is { } sid && sid != IdTiendaEpic) continue;
            string? titulo = Texto(d, "title");
            if (!NormalizadorNombres.CoincideConJuego(juego, titulo)) continue;

            decimal? venta = Decimal(d, "salePrice"), normal = Decimal(d, "normalPrice");
            string? dealId = Texto(d, "dealID");
            if (venta is null || normal is null || string.IsNullOrEmpty(dealId))
                throw new ScraperException(TipoError.FormatoCambiado, $"una oferta de CheapShark para '{titulo}' no trae salePrice/normalPrice/dealID");

            lista.Add(new OfertaCs(titulo!, venta.Value, Math.Max(normal.Value, venta.Value), dealId!, Texto(d, "thumb")));
        }
        return (lista, evaluados);
    }

    /// <summary>CheapShark ya entrega el dealID codificado para URL (termina en %3D); solo se codifica si trae otros caracteres.</summary>
    private static string IdSeguro(string id) =>
        Regex.IsMatch(id, "^[A-Za-z0-9%._-]+$") ? id : Uri.EscapeDataString(id);

    private static string? Texto(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? TextoONumero(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null
        } : null;

    /// <summary>Los precios llegan como texto ("19.99"); se acepta también número. Siempre con punto decimal (InvariantCulture).</summary>
    private static decimal? Decimal(JsonElement e, string prop)
    {
        string? t = TextoONumero(e, prop);
        return t != null && decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out var n) && n >= 0 ? n : null;
    }
}
