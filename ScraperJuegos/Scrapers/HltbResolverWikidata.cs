using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScraperJuegos;

/// <summary>Resultado de intentar descubrir el hltb_id de un juego. Estado: ok | no_encontrado | ambiguo | timeout | bloqueo | formato_cambiado.</summary>
public sealed record ResolucionHltb(string? HltbId, string Estado, string Detalle, string? Qid = null);

/// <summary>
/// Descubre el hltb_id (el número de https://howlongtobeat.com/game/ID) de un juego SIN usar el buscador de HLTB.
///
/// POR QUÉ: el robots.txt de HLTB prohíbe /api (su buscador), así que el id no se puede buscar en HLTB. Pero Wikidata
/// mantiene, para muchos videojuegos, la propiedad "HowLongToBeat ID" (por defecto P2816; configurable con la variable
/// de entorno HLTB_WIKIDATA_PROP). Wikidata ofrece una API pública pensada para uso programático con User-Agent descriptivo.
///
/// ESTRATEGIA (no se adivina):
///  1. wbsearchentities: busca entidades con el nombre del juego (hasta 10 candidatas).
///  2. El nombre (label) debe coincidir con el del juego tras normalizar (mismo criterio que el resto del proyecto).
///  3. Si la descripción trae un año (ej. "2022 action role-playing game") y difiere en más de 1 año del REPO, se descarta
///     (así un remake no se confunde con el original).
///  4. wbgetentities: se lee la propiedad del id de HLTB. Si queda exactamente UN id distinto -> ok.
///     Si no hay ninguno -> no_encontrado; si hay varios distintos -> ambiguo (se deja para llenar a mano).
/// El id descubierto igual se valida después con "hltb-verificar" (la página /game/ID debe llamarse como el juego).
/// Sin estado compartido; nunca lanza (salvo cancelación).
/// </summary>
public sealed class ResolverHltbWikidata
{
    public const string Fuente = "Wikidata";
    private const string Api = "https://www.wikidata.org/w/api.php";
    private static readonly Regex Anio = new(@"\b(19[7-9]\d|20\d\d)\b", RegexOptions.Compiled);

    private readonly ClienteHttp _http;
    private readonly string _propiedad;

    public ResolverHltbWikidata(ClienteHttp http, string? propiedad = null)
    {
        _http = http;
        _propiedad = propiedad
            ?? Environment.GetEnvironmentVariable("HLTB_WIKIDATA_PROP")
            ?? "P2816";
    }

    public async Task<ResolucionHltb> ResolverAsync(Juego juego, CancellationToken ct = default)
    {
        try { return await InternoAsync(juego, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (ScraperException ex)
        {
            _http.Log.Registrar(juego.Nombre, juego.Plataforma, Fuente, ex.Tipo, ex.Message);
            return new ResolucionHltb(null, ex.Tipo, ex.Message);
        }
        catch (Exception ex)
        {
            string det = $"{ex.GetType().Name}: {ex.Message}";
            _http.Log.Registrar(juego.Nombre, juego.Plataforma, Fuente, TipoError.FormatoCambiado, det);
            return new ResolucionHltb(null, TipoError.FormatoCambiado, det);
        }
    }

    private async Task<ResolucionHltb> InternoAsync(Juego juego, CancellationToken ct)
    {
        var ctx = new ContextoLog(juego.Nombre, juego.Plataforma, Fuente);
        var cab = new Dictionary<string, string> { ["Accept"] = "application/json" };

        // 1) Buscar entidades por nombre.
        string urlBusqueda = $"{Api}?action=wbsearchentities&search={Uri.EscapeDataString(juego.NombreParaBuscar)}" +
                             "&language=en&uselang=en&type=item&limit=10&format=json";
        var rb = await _http.ObtenerAsync(urlBusqueda, ctx, ct, cab).ConfigureAwait(false);

        var candidatos = new List<(string qid, string label, string desc)>();
        using (var doc = JsonDocument.Parse(rb.Cuerpo))
        {
            if (!doc.RootElement.TryGetProperty("search", out var arr) || arr.ValueKind != JsonValueKind.Array)
                throw new ScraperException(TipoError.FormatoCambiado, "Wikidata: la respuesta de búsqueda no trae el arreglo 'search'");
            foreach (var e in arr.EnumerateArray())
            {
                string? qid = Texto(e, "id"), label = Texto(e, "label"), desc = Texto(e, "description") ?? "";
                if (qid is null || label is null) continue;
                string alias = e.TryGetProperty("match", out var m) ? (Texto(m, "text") ?? "") : "";
                // No se filtra por la palabra "video game" (muchas descripciones dicen solo "action role-playing game"):
                // basta con que la entidad tenga el id de HLTB, que solo existe en juegos.
                if (!NormalizadorNombres.CoincideConJuego(juego, label) && !NormalizadorNombres.CoincideConJuego(juego, alias)) continue;
                if (!AnioCompatible(juego, desc)) continue;
                candidatos.Add((qid, label, desc));
            }
        }
        if (candidatos.Count == 0)
            return new ResolucionHltb(null, TipoError.NoEncontrado, "Wikidata no tiene un videojuego con ese nombre y año");

        // 2) Leer la propiedad del id de HLTB de los candidatos.
        string ids = string.Join('|', candidatos.Select(c => c.qid).Distinct());
        string urlEntidades = $"{Api}?action=wbgetentities&ids={Uri.EscapeDataString(ids)}&props=claims&format=json";
        var re = await _http.ObtenerAsync(urlEntidades, ctx, ct, cab).ConfigureAwait(false);

        var encontrados = new Dictionary<string, string>(); // hltb_id -> qid
        using (var doc = JsonDocument.Parse(re.Cuerpo))
        {
            if (!doc.RootElement.TryGetProperty("entities", out var ents) || ents.ValueKind != JsonValueKind.Object)
                throw new ScraperException(TipoError.FormatoCambiado, "Wikidata: la respuesta no trae 'entities'");
            foreach (var ent in ents.EnumerateObject())
            {
                if (!ent.Value.TryGetProperty("claims", out var claims) || claims.ValueKind != JsonValueKind.Object) continue;
                if (!claims.TryGetProperty(_propiedad, out var lista) || lista.ValueKind != JsonValueKind.Array) continue;
                foreach (var c in lista.EnumerateArray())
                {
                    if (Texto(c, "rank") == "deprecated") continue;
                    if (!c.TryGetProperty("mainsnak", out var ms) || !ms.TryGetProperty("datavalue", out var dv)
                        || !dv.TryGetProperty("value", out var v)) continue;
                    string? valor = v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString();
                    if (!string.IsNullOrWhiteSpace(valor) && valor.Trim().All(char.IsDigit))
                        encontrados.TryAdd(valor.Trim(), ent.Name);
                }
            }
        }
        if (encontrados.Count == 0)
            return new ResolucionHltb(null, TipoError.NoEncontrado, $"Wikidata conoce el juego ({string.Join(", ", candidatos.Select(c => c.qid))}) pero no tiene su id de HLTB ({_propiedad})");
        if (encontrados.Count > 1)
            return new ResolucionHltb(null, "ambiguo", $"varios ids posibles en Wikidata: {string.Join(", ", encontrados.Select(kv => $"{kv.Key} ({kv.Value})"))}; elija a mano");
        var unico = encontrados.First();
        return new ResolucionHltb(unico.Key, "ok", $"id {unico.Key} desde Wikidata {unico.Value}", unico.Value);
    }

    private static bool AnioCompatible(Juego juego, string descripcion)
    {
        if (juego.FechaLanzamiento.Year < 1970) return true; // sin año confiable en el REPO
        var m = Anio.Match(descripcion);
        if (!m.Success) return true;                          // la descripción no trae año: no se descarta
        return Math.Abs(int.Parse(m.Value) - juego.FechaLanzamiento.Year) <= 1;
    }

    private static string? Texto(JsonElement e, string prop) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
