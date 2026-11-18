using System.Globalization;
using System.Text.Json;

namespace ScraperJuegos;

/// <summary>
/// Tienda 2: Epic Games Store (solo PC, formato digital, USD), consultada VÍA CheapShark.
/// Epic bloquea (403) a los clientes automáticos de su propia tienda, así que no se le pide nada a store.epicgames.com:
/// los precios de Epic se leen de la API pública y gratuita de CheapShark (https://apidocs.cheapshark.com), que no requiere clave.
///
/// Flujo (máximo 3 solicitudes por juego; la primera se hace una sola vez por corrida):
///   1. GET /api/1.0/stores            -> busca por NOMBRE "Epic Games Store" y obtiene su storeID (no se escribe a mano: los ids de
///                                        tienda de CheapShark han cambiado con el tiempo). Se recuerda mientras viva este objeto.
///                                        Se puede fijar con CHEAPSHARK_EPIC_STORE_ID (omite esta solicitud).
///   2. GET /api/1.0/deals?storeID=ID&amp;title=NOMBRE   -> ofertas de Epic cuyo título coincide con el juego.
///   3. (solo si 2 no encontró nada y el REPO trae steam_appid) GET /api/1.0/deals?storeID=ID&amp;steamAppID=N.
///
/// Reglas de cortesía de CheapShark que se cumplen aquí:
///   - Se respeta su robots.txt como en todo el proyecto (si no permite /api -> bloqueo, sin hacer la solicitud).
///   - Pausa de 1 s por dominio (LimitadorDominio) y reintento con espera si responde 429.
///   - Si CheapShark bloquea (403/429 persistente/robots), este scraper deja de insistir durante unos minutos (disyuntor):
///     Epic queda sin precios, el resto del programa sigue normal.
///   - El enlace que se entrega es el de su /redirect?dealID=..., que es el que CheapShark pide usar.
///
/// RESPALDO: si CheapShark falla (bloqueo, formato cambiado o el juego no aparece) y existe la variable ITAD_API_KEY, Epic se consulta
/// con la API oficial de IsThereAnyDeal (ver ItadEpicFuente). Sin clave no se hace ninguna solicitud a ITAD.
///
/// Los precios llegan como texto decimal ("35.99") en USD. Sin estado por solicitud (solo el id de tienda y el disyuntor, protegidos).
/// </summary>
public sealed class EpicGamesScraper : ITiendaScraper
{
    public string Tienda => "Epic Games Store";
    public string Dominio => "www.cheapshark.com";
    private readonly ClienteHttp _http;
    private readonly ItadEpicFuente _itad;

    internal const string UrlBase = "https://www.cheapshark.com/api/1.0";
    internal const string UrlRedirect = "https://www.cheapshark.com/redirect?dealID=";
    private static readonly TimeSpan DuracionDisyuntor = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _candado = new(1, 1);
    private string? _storeId;                 // se llena una vez (o viene de la configuración)
    private DateTime _bloqueadoHastaUtc;      // disyuntor
    private string _detalleBloqueo = "";

    public EpicGamesScraper(ClienteHttp http)
    {
        _http = http;
        _itad = new ItadEpicFuente(http);
        if (!string.IsNullOrWhiteSpace(http.Config.CheapSharkEpicStoreId) && http.Config.CheapSharkEpicStoreId!.Trim().All(char.IsDigit))
            _storeId = http.Config.CheapSharkEpicStoreId.Trim();
    }

    public bool SoportaPlataforma(string plataforma) => plataforma == "PC";

    public Task<ResultadoTienda> ConsultarAsync(Juego juego, CancellationToken ct = default) =>
        Seguro.EjecutarAsync(() => ConsultarInternoAsync(juego, ct),
            (tipo, det) => ResultadoTienda.Fallo(Tienda, tipo, det), _http.Log, juego, Tienda, ct);

    private sealed record OfertaEpic(string Titulo, decimal Actual, decimal Regular, string? DealId, string? Imagen);

    private async Task<ResultadoTienda> ConsultarInternoAsync(Juego juego, CancellationToken ct)
    {
        if (!SoportaPlataforma(juego.Plataforma))
            throw new ScraperException(TipoError.NoEncontrado, $"Epic Games Store solo vende para PC (juego en {juego.Plataforma})");

        ScraperException falloCheapShark;
        try { return await ConsultarCheapSharkAsync(juego, ct).ConfigureAwait(false); }
        catch (ScraperException ex) { falloCheapShark = ex; }

        // Respaldo: API oficial de IsThereAnyDeal (solo con ITAD_API_KEY).
        if (!_itad.Configurada)
        {
            // Un "no encontrado" normal no necesita sugerencias; un bloqueo o cambio de formato sí.
            if (falloCheapShark.Tipo == TipoError.NoEncontrado) throw falloCheapShark;
            throw new ScraperException(falloCheapShark.Tipo, falloCheapShark.Message + " | respaldo ITAD no configurado: defina ITAD_API_KEY");
        }
        try
        {
            return await _itad.ConsultarAsync(Tienda, juego, new ContextoLog(juego.Nombre, juego.Plataforma, Tienda), ct).ConfigureAwait(false);
        }
        catch (ScraperException exItad)
        {
            // Si ITAD también falla se reportan los dos motivos; el tipo es el de ITAD salvo que solo diga "no encontrado".
            string tipo = exItad.Tipo == TipoError.NoEncontrado ? falloCheapShark.Tipo : exItad.Tipo;
            throw new ScraperException(tipo, $"CheapShark: {falloCheapShark.Message} | ITAD: {exItad.Message}");
        }
    }

    private async Task<ResultadoTienda> ConsultarCheapSharkAsync(Juego juego, CancellationToken ct)
    {
        if (DateTime.UtcNow < _bloqueadoHastaUtc)
            throw new ScraperException(TipoError.Bloqueo, $"CheapShark bloqueó antes en esta corrida; no se insiste ({_detalleBloqueo})");

        var ctx = new ContextoLog(juego.Nombre, juego.Plataforma, Tienda);
        try
        {
            string storeId = await ObtenerStoreIdAsync(ctx, ct).ConfigureAwait(false);

            // 2) por título
            string url = $"{UrlBase}/deals?storeID={storeId}&title={Uri.EscapeDataString(juego.NombreParaBuscar)}&pageSize=20";
            var candidatas = LeerOfertas(await PedirJsonAsync(url, ctx, ct).ConfigureAwait(false), storeId, juego, porSteamAppId: false, out int evaluadas);

            // 3) respaldo por steam_appid (identificador exacto, no depende de cómo se escriba el título)
            if (candidatas.Count == 0 && !string.IsNullOrWhiteSpace(juego.SteamAppId) && juego.SteamAppId!.Trim().All(char.IsDigit))
            {
                string url2 = $"{UrlBase}/deals?storeID={storeId}&steamAppID={juego.SteamAppId.Trim()}&pageSize=20";
                candidatas = LeerOfertas(await PedirJsonAsync(url2, ctx, ct).ConfigureAwait(false), storeId, juego, porSteamAppId: true, out int ev2);
                evaluadas += ev2;
            }

            if (candidatas.Count == 0)
                throw new ScraperException(TipoError.NoEncontrado,
                    $"ninguna oferta de Epic Games Store (vía CheapShark) coincide con '{juego.NombreParaBuscar}' (se evaluaron {evaluadas})");

            // Varias ediciones (estándar/deluxe): la base es la de menor precio regular (y de ahí la más barata hoy).
            var elegida = candidatas.OrderBy(c => c.Regular).ThenBy(c => c.Actual).First();
            if (elegida.Regular <= 0)
                throw new ScraperException(TipoError.NoEncontrado, "el producto es gratuito o no muestra precio en Epic Games Store");

            string enlace = elegida.DealId != null ? UrlRedirect + EnlaceDealId(elegida.DealId)
                                                  : "https://www.cheapshark.com/browse?title=" + Uri.EscapeDataString(juego.NombreParaBuscar);
            return ResultadoTienda.Exito(Tienda, elegida.Actual, elegida.Regular, Catalogos.Digital, "PC", enlace, elegida.Imagen);
        }
        catch (ScraperException ex) when (ex.Tipo == TipoError.Bloqueo)
        {
            _bloqueadoHastaUtc = DateTime.UtcNow + DuracionDisyuntor;
            _detalleBloqueo = ex.Message;
            throw;
        }
    }

    private async Task<string> ObtenerStoreIdAsync(ContextoLog ctx, CancellationToken ct)
    {
        if (_storeId != null) return _storeId;
        await _candado.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_storeId != null) return _storeId;
            using var doc = await PedirJsonAsync($"{UrlBase}/stores", ctx, ct).ConfigureAwait(false);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                throw new ScraperException(TipoError.FormatoCambiado, "CheapShark /stores no devolvió una lista de tiendas");

            bool existe = false;
            foreach (var t in doc.RootElement.EnumerateArray())
            {
                string? nombre = Cadena(t, "storeName");
                if (nombre is null || !nombre.Contains("epic", StringComparison.OrdinalIgnoreCase)) continue;
                existe = true;
                string? id = Cadena(t, "storeID");
                bool activa = Cadena(t, "isActive") is not ("0" or "false");
                if (activa && !string.IsNullOrWhiteSpace(id) && id!.All(char.IsDigit)) { _storeId = id; return id; }
            }
            throw new ScraperException(TipoError.FormatoCambiado, existe
                ? "CheapShark lista a Epic Games Store pero como tienda inactiva (ya no sigue sus precios)"
                : "CheapShark ya no lista a Epic Games Store entre sus tiendas");
        }
        finally { _candado.Release(); }
    }

    private async Task<JsonDocument> PedirJsonAsync(string url, ContextoLog ctx, CancellationToken ct)
    {
        var resp = await _http.ObtenerAsync(url, ctx, ct, new Dictionary<string, string> { ["Accept"] = "application/json" }).ConfigureAwait(false);
        try { return JsonDocument.Parse(resp.Cuerpo); }
        catch (JsonException) { throw new ScraperException(TipoError.FormatoCambiado, "CheapShark no devolvió JSON válido"); }
    }

    // Sobrecarga: lee y libera el documento.
    private List<OfertaEpic> LeerOfertas(JsonDocument doc, string storeId, Juego juego, bool porSteamAppId, out int evaluadas)
    {
        using (doc)
        {
            evaluadas = 0;
            var lista = new List<OfertaEpic>();
            var raiz = doc.RootElement;
            if (raiz.ValueKind != JsonValueKind.Array)
                throw new ScraperException(TipoError.FormatoCambiado, "CheapShark /deals no devolvió una lista de ofertas");

            foreach (var el in raiz.EnumerateArray())
            {
                evaluadas++;
                if (Cadena(el, "storeID") != storeId) continue;   // por si el filtro de tienda no se aplicara
                string? titulo = Cadena(el, "title");
                if (!porSteamAppId && !NormalizadorNombres.CoincideConJuego(juego, titulo)) continue;
                if (porSteamAppId && Cadena(el, "steamAppID") != juego.SteamAppId!.Trim()) continue;

                decimal? actual = Decimal(el, "salePrice"), regular = Decimal(el, "normalPrice");
                if (actual is null || regular is null) continue;
                string? thumb = Cadena(el, "thumb");
                lista.Add(new OfertaEpic(titulo ?? juego.Nombre, actual.Value, Math.Max(regular.Value, actual.Value),
                                         Cadena(el, "dealID"), thumb is { Length: > 0 } && thumb.StartsWith("https://") ? thumb : null));
            }
            return lista;
        }
    }

    /// <summary>
    /// El dealID de CheapShark ya viene codificado para URL (termina en %3D, trae %2B...). Se usa tal cual, como indica su documentación,
    /// para no codificarlo dos veces; si trajera caracteres inesperados, ahí sí se codifica.
    /// </summary>
    private static string EnlaceDealId(string id) =>
        id.All(c => char.IsAsciiLetterOrDigit(c) || c is '%' or '-' or '_' or '.' or '~') ? id : Uri.EscapeDataString(id);

    private static string? Cadena(JsonElement e, string prop)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(prop, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static decimal? Decimal(JsonElement e, string prop) =>
        Cadena(e, prop) is { } s && decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) && d >= 0 ? d : null;
}
