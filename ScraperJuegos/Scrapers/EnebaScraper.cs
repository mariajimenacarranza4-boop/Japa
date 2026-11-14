using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScraperJuegos;

/// <summary>
/// Tienda 4: Eneba (mercado de CLAVES digitales de vendedores terceros; formato digital, USD).
/// Eneba no ofrece API pública sin registro: se lee la página de búsqueda (https://www.eneba.com/store/all?text=...) y,
/// de ella, SOLO los datos estructurados estándar (schema.org: bloques JSON-LD y __NEXT_DATA__), porque en ellos el precio y la
/// moneda son inequívocos (nunca se interpreta un número "en centavos" por suposición).
/// Reglas propias de un mercado de claves (no se adivina nada):
///  - El nombre trae sufijos ("Elden Ring (PC) Steam Key GLOBAL"): se limpia para compararlo y de él se deduce la PLATAFORMA;
///    si no se puede confirmar la plataforma -> no_encontrado.
///  - Solo claves GLOBALES o sin región (se descartan EU / US / UK / LATAM / ASIA...), porque una clave de otra región puede no activarse.
///  - Solo precios en USD (si Eneba responde en otra moneda -> formato_cambiado; jamás se mezclan monedas con las otras tiendas).
///  - Eneba no tiene "precio regular" (es un mercado): precio_regular se omite y la oferta se evalúa contra el precio_sugerido del REPO.
/// Es la fuente MÁS propensa a bloqueo (suele usar anti-bots): bloqueo/formato_cambiado son resultados esperables y controlados.
/// Sin estado compartido: 1 solicitud por juego.
/// </summary>
public sealed class EnebaScraper : ITiendaScraper
{
    public string Tienda => "Eneba";
    public string Dominio => "www.eneba.com";
    private readonly ClienteHttp _http;

    private const string BaseSitio = "https://www.eneba.com";

    private static readonly Regex BloquesJson = new(
        "<script[^>]*(?:type=\"application/(?:ld\\+)?json\"|id=\"__NEXT_DATA__\")[^>]*>(?<json>.*?)</script>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Desde la primera palabra de plataforma/tienda/tipo de clave en adelante, el nombre ya no es del juego.
    private static readonly Regex CorteNombre = new(
        @"\s(?:PC|Steam|Epic Games|EA App|Origin|GOG|Xbox|PlayStation|PS4|PS5|Nintendo|Switch|Ubisoft Connect|Battle\.net|Rockstar|Key|CD Key|Account|Gift Card)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Regiones (en mayúsculas, como las escribe Eneba). "Us" en "The Last of Us" NO coincide porque se exige mayúscula.
    private static readonly Regex Region = new(@"\b(?:EU|UK|USA|US|EUROPE|LATAM|ASIA|CIS)\b", RegexOptions.Compiled);

    // Eneba (2026) NO trae JSON-LD: los resultados vienen en <script id="__ALGOLIA_STATE__"> (hits con lowestPrice en centavos).
    private static readonly Regex BloqueAlgolia = new(
        "<script[^>]*id=\"__ALGOLIA_STATE__\"[^>]*>(?<json>.*?)</script>", RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>
    /// Regiones de clave que se aceptan (productRegions de Eneba). Por defecto GLOBAL y LATAM: el equipo está en Costa Rica y
    /// una clave LATAM se activa allí. Se cambia sin recompilar: variable de entorno ENEBA_REGIONES (ej. "global" para ser estrictos).
    /// </summary>
    private static readonly HashSet<string> RegionesAceptadas = new(
        (Environment.GetEnvironmentVariable("ENEBA_REGIONES") ?? "global,latam")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        StringComparer.OrdinalIgnoreCase);

    public EnebaScraper(ClienteHttp http) => _http = http;

    public bool SoportaPlataforma(string plataforma) => Catalogos.Plataformas.Contains(plataforma);

    public Task<ResultadoTienda> ConsultarAsync(Juego juego, CancellationToken ct = default) =>
        Seguro.EjecutarAsync(() => ConsultarInternoAsync(juego, ct),
            (tipo, det) => ResultadoTienda.Fallo(Tienda, tipo, det), _http.Log, juego, Tienda, ct);

    private sealed record Producto(string Nombre, decimal Precio, string? Moneda, string? Enlace, string? Imagen,
                                   string[]? Regiones = null, decimal? Regular = null, string? Tipo = null, string? Drm = null);

    private async Task<ResultadoTienda> ConsultarInternoAsync(Juego juego, CancellationToken ct)
    {
        if (!SoportaPlataforma(juego.Plataforma))
            throw new ScraperException(TipoError.NoEncontrado, $"Eneba no tiene equivalente para la plataforma '{juego.Plataforma}'");

        var ctx = new ContextoLog(juego.Nombre, juego.Plataforma, Tienda);
        // currency=USD: se pide USD; si Eneba lo ignora y responde otra moneda, se rechaza más abajo.
        string url = BaseSitio + "/store/all?text=" + Uri.EscapeDataString(juego.NombreParaBuscar) + "&currency=USD";
        var resp = await _http.ObtenerAsync(url, ctx, ct).ConfigureAwait(false);

        var productos = LeerAlgolia(resp.Cuerpo, out bool hayAlgolia);
        int bloquesLeidos = hayAlgolia ? 1 : 0;
        if (hayAlgolia && productos.Count == 0)
            throw new ScraperException(TipoError.NoEncontrado,
                $"Eneba no devolvió resultados con precio y en stock para '{juego.NombreParaBuscar}'");
        if (productos.Count == 0)
        {
            // Plan B: datos estructurados schema.org (JSON-LD / __NEXT_DATA__), por si Eneba vuelve a ese formato.
            foreach (Match m in BloquesJson.Matches(resp.Cuerpo))
            {
                try
                {
                    using var doc = JsonDocument.Parse(m.Groups["json"].Value);
                    bloquesLeidos++;
                    Recorrer(doc.RootElement, productos);
                }
                catch (JsonException) { /* un bloque dañado no impide leer los demás */ }
            }
        }
        if (bloquesLeidos == 0)
            throw new ScraperException(TipoError.FormatoCambiado, "la página de Eneba no trae bloques JSON (JSON-LD / __NEXT_DATA__)");
        if (productos.Count == 0)
            throw new ScraperException(TipoError.FormatoCambiado,
                "los bloques JSON de Eneba no contienen productos con precio y moneda (¿cambió el formato o no hubo resultados?)");

        var coinciden = productos.Where(p => (p.Tipo is null || p.Tipo == "game")   // sin DLC
                                      && NormalizadorNombres.CoincideConJuego(juego, NombreBase(p.Nombre))).ToList();
        if (coinciden.Count == 0)
            throw new ScraperException(TipoError.NoEncontrado,
                $"ningún resultado de Eneba coincide con '{juego.NombreParaBuscar}' (se evaluaron {productos.Count})");

        var enPlataforma = coinciden.Where(p => EsDePlataforma(p, juego.Plataforma)).ToList();
        if (enPlataforma.Count == 0)
            throw new ScraperException(TipoError.NoEncontrado,
                $"'{juego.Nombre}' aparece en Eneba pero no se pudo confirmar una clave para {juego.Plataforma} (no se adivina). " +
                $"Títulos vistos: {string.Join(" | ", coinciden.Select(p => p.Nombre).Distinct().Take(4))}");

        var globales = enPlataforma.Where(RegionAceptable).ToList();
        if (globales.Count == 0)
            throw new ScraperException(TipoError.NoEncontrado,
                $"las claves de '{juego.Nombre}' para {juego.Plataforma} en Eneba tienen restricción de región (solo se aceptan: {string.Join(',', RegionesAceptadas)})");

        var enUsd = globales.Where(p => string.Equals(p.Moneda, "USD", StringComparison.OrdinalIgnoreCase)).ToList();
        if (enUsd.Count == 0)
            throw new ScraperException(TipoError.FormatoCambiado,
                $"Eneba devolvió precios en {globales[0].Moneda ?? "una moneda desconocida"} y no en USD; no se mezclan monedas");

        var elegido = enUsd.OrderBy(p => p.Precio).First();
        // Eneba informa un "msrp" (precio de referencia) en algunos productos; si no, no hay precio regular y la oferta
        // se evalúa contra el precio sugerido del REPO.
        decimal? regular = elegido.Regular is > 0 && elegido.Regular >= elegido.Precio ? elegido.Regular : null;
        return ResultadoTienda.Exito(Tienda, elegido.Precio, regular ?? elegido.Precio, Catalogos.Digital, juego.Plataforma,
                   elegido.Enlace ?? url, elegido.Imagen) with { PrecioRegular = regular };
    }

    // ---- Limpieza y reglas del mercado de claves (internal static para poder probarlas sin internet) ----

    /// <summary>"Elden Ring (PC) Steam Key GLOBAL" -> "Elden Ring".</summary>
    internal static string NombreBase(string? crudo)
    {
        if (string.IsNullOrWhiteSpace(crudo)) return "";
        string t = Regex.Replace(crudo, @"\([^)]*\)", " ");
        var m = CorteNombre.Match(t);
        if (m.Success) t = t[..m.Index];
        return t.Trim();
    }

    /// <summary>Plataforma (como en el REPO) que indica el nombre de la clave, o null si no se puede confirmar.</summary>
    internal static string? DetectarPlataforma(string? crudo)
    {
        if (string.IsNullOrWhiteSpace(crudo)) return null;
        string t = " " + crudo.ToLowerInvariant() + " ";
        if (t.Contains("xbox series")) return "Xbox Series";
        if (t.Contains("xbox one")) return "Xbox One";
        if (t.Contains("ps5") || t.Contains("playstation 5")) return "PS5";
        if (t.Contains("ps4") || t.Contains("playstation 4")) return "PS4";
        if (t.Contains("switch")) return "Switch";
        if (t.Contains("(pc)") || t.Contains(" pc ") || t.Contains("steam") || t.Contains("epic games") || t.Contains("ea app")
            || t.Contains("gog") || t.Contains("ubisoft connect") || t.Contains("battle.net") || t.Contains("rockstar")) return "PC";
        return null;
    }

    /// <summary>¿La clave sirve para esa plataforma? Por el nombre; una clave "XBOX LIVE" sin generación en el nombre sirve para Xbox One y Series.</summary>
    private static bool EsDePlataforma(Producto p, string plataforma)
    {
        string? detectada = DetectarPlataforma(p.Nombre);
        if (detectada != null) return detectada == plataforma;
        if (string.Equals(p.Drm, "xbox", StringComparison.OrdinalIgnoreCase)) return plataforma is "Xbox Series" or "Xbox One";
        return false;
    }

    /// <summary>Con productRegions (Algolia) se usa esa lista; sin ella (JSON-LD) se deduce del nombre y solo se aceptan claves sin región.</summary>
    private static bool RegionAceptable(Producto p) =>
        p.Regiones is { Length: > 0 } r ? r.Any(x => RegionesAceptadas.Contains(x)) : !TieneRegion(p.Nombre);

    internal static bool TieneRegion(string? crudo) => !string.IsNullOrEmpty(crudo) && Region.IsMatch(crudo);

    // ---- Lectura del estado de búsqueda de Eneba (__ALGOLIA_STATE__) ----

    private static List<Producto> LeerAlgolia(string html, out bool hayBloque)
    {
        var lista = new List<Producto>();
        var m = BloqueAlgolia.Match(html);
        hayBloque = m.Success;
        if (!m.Success) return lista;
        try
        {
            using var doc = JsonDocument.Parse(m.Groups["json"].Value);
            BuscarHits(doc.RootElement, lista);
        }
        catch (JsonException) { /* bloque dañado: se trata como sin productos */ }
        return lista;
    }

    private static void BuscarHits(JsonElement e, List<Producto> acumulado)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in e.EnumerateObject())
                {
                    if (prop.Name == "hits" && prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var h in prop.Value.EnumerateArray())
                            if (HitAProducto(h) is { } p) acumulado.Add(p);
                    }
                    else BuscarHits(prop.Value, acumulado);
                }
                break;
            case JsonValueKind.Array:
                foreach (var x in e.EnumerateArray()) BuscarHits(x, acumulado);
                break;
        }
    }

    /// <summary>Un resultado de Eneba -> Producto. Precios en la unidad menor (4676 = 46.76). Sin stock o sin precio -> null.</summary>
    private static Producto? HitAProducto(JsonElement h)
    {
        if (h.ValueKind != JsonValueKind.Object) return null;
        if (h.TryGetProperty("stockAvailable", out var st) && st.ValueKind == JsonValueKind.False) return null;

        string? nombre = null;
        if (h.TryGetProperty("translations", out var tr) && tr.ValueKind == JsonValueKind.Object)
        {
            foreach (string idioma in new[] { "en", "en_US" })
            {
                if (tr.TryGetProperty(idioma, out var t) && Texto(t, "name") is { Length: > 0 } n) { nombre = n; break; }
            }
        }
        if (string.IsNullOrWhiteSpace(nombre)) return null;

        decimal? precio = null; string? moneda = null;
        if (h.TryGetProperty("lowestPrice", out var lp) && lp.ValueKind == JsonValueKind.Object)
        {
            if (Centavos(lp, "USD") is { } usd) { precio = usd; moneda = "USD"; }
            else
            {
                foreach (var c in lp.EnumerateObject())
                    if (Centavos(lp, c.Name) is { } otro) { precio = otro; moneda = c.Name; break; }
            }
        }
        if (precio is null or <= 0 || moneda is null) return null;

        decimal? regular = null;
        if (moneda == "USD" && h.TryGetProperty("msrp", out var ms) && ms.ValueKind == JsonValueKind.Object) regular = Centavos(ms, "USD");

        string[]? regiones = null;
        if (h.TryGetProperty("productRegions", out var pr) && pr.ValueKind == JsonValueKind.Array)
            regiones = pr.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray();

        string? imagen = null;
        if (h.TryGetProperty("images", out var im) && im.ValueKind == JsonValueKind.Object
            && im.TryGetProperty("cover300", out var c3)) imagen = Texto(c3, "src");

        string? slug = Texto(h, "slug");
        return new Producto(nombre!, precio.Value, moneda, string.IsNullOrWhiteSpace(slug) ? null : BaseSitio + "/" + slug, imagen,
                            regiones, regular, Texto(h, "productType"), Texto(h, "drmName"));
    }

    private static decimal? Centavos(JsonElement e, string prop)
    {
        decimal? n = Numero(e, prop);
        return n is > 0 ? n / 100m : null;
    }

    // ---- Lectura de datos estructurados (schema.org Product + Offer) ----

    private static void Recorrer(JsonElement e, List<Producto> acumulado)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                if (EsProducto(e, out var p) && p != null) acumulado.Add(p);
                foreach (var prop in e.EnumerateObject()) Recorrer(prop.Value, acumulado);
                break;
            case JsonValueKind.Array:
                foreach (var x in e.EnumerateArray()) Recorrer(x, acumulado);
                break;
        }
    }

    private static bool EsProducto(JsonElement o, out Producto? p)
    {
        p = null;
        string? nombre = Texto(o, "name");
        if (string.IsNullOrWhiteSpace(nombre)) return false;
        if (!o.TryGetProperty("offers", out var offers)) return false;

        JsonElement oferta = offers;
        if (offers.ValueKind == JsonValueKind.Array)
        {
            if (offers.GetArrayLength() == 0) return false;
            oferta = offers[0];
        }
        if (oferta.ValueKind != JsonValueKind.Object) return false;

        decimal? precio = Numero(oferta, "price") ?? Numero(oferta, "lowPrice");
        string? moneda = Texto(oferta, "priceCurrency");
        if (precio is null or <= 0 || string.IsNullOrWhiteSpace(moneda)) return false;

        string? enlace = Absoluta(Texto(o, "url") ?? Texto(oferta, "url"));
        string? imagen = null;
        if (o.TryGetProperty("image", out var img))
        {
            if (img.ValueKind == JsonValueKind.String) imagen = img.GetString();
            else if (img.ValueKind == JsonValueKind.Array && img.GetArrayLength() > 0 && img[0].ValueKind == JsonValueKind.String)
                imagen = img[0].GetString();
        }
        p = new Producto(nombre!, precio.Value, moneda, enlace, Absoluta(imagen));
        return true;
    }

    private static string? Absoluta(string? u)
    {
        if (string.IsNullOrWhiteSpace(u)) return null;
        if (u.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return u;
        return BaseSitio + (u.StartsWith('/') ? "" : "/") + u;
    }

    private static string? Texto(JsonElement e, string prop) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static decimal? Numero(JsonElement e, string prop)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(prop, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d)) return d;
        if (v.ValueKind == JsonValueKind.String &&
            decimal.TryParse(v.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var s)) return s;
        return null;
    }
}
