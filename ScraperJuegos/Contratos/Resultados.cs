using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScraperJuegos;

/// <summary>Los 4 tipos de error controlado del Contrato 1.</summary>
public static class TipoError
{
    public const string Timeout = "timeout";                 // sin conexión, timeout o servidor caído (tras los reintentos)
    public const string Bloqueo = "bloqueo";                 // 403/429/captcha, o robots.txt no lo permite
    public const string FormatoCambiado = "formato_cambiado"; // el dato esperado no apareció en la respuesta
    public const string NoEncontrado = "no_encontrado";      // el juego no existe en la fuente o no hay coincidencia segura
}

/// <summary>
/// CONTRATO 1 (B entrega, A consume). Se serializa con JSON snake_case y sin nulos:
///   ok:true  -> { ok, tienda, precio_actual, precio_regular, formato, plataforma, enlace, imagen_url, moneda, fecha_consulta }
///   ok:false -> { ok, tienda, error, detalle, fecha_consulta }
/// (tienda, imagen_url, moneda y detalle son campos AGREGADOS; no quitan nada del contrato original.)
/// </summary>
public record ResultadoTienda(
    bool Ok,
    string Tienda,
    decimal? PrecioActual,
    decimal? PrecioRegular,
    string? Formato,
    string? Plataforma,
    string? Enlace,
    string? ImagenUrl,
    string? Moneda,
    string? Error,
    string? Detalle,
    DateTimeOffset FechaConsulta)
{
    public static ResultadoTienda Exito(string tienda, decimal actual, decimal regular, string formato,
        string plataforma, string enlace, string? imagen, string moneda = "USD") =>
        new(true, tienda, actual, regular, formato, plataforma, enlace, imagen, moneda, null, null, DateTimeOffset.UtcNow);

    public static ResultadoTienda Fallo(string tienda, string tipoError, string? detalle = null) =>
        new(false, tienda, null, null, null, null, null, null, null, tipoError, detalle, DateTimeOffset.UtcNow);
}

/// <summary>{ ok, score, plataforma, fecha_consulta }. Verificada=false: la plataforma no se pudo confirmar en la página.</summary>
public record ResultadoMetacritic(
    bool Ok,
    int? Score,
    string? Plataforma,
    bool? Verificada,
    string? Enlace,
    string? Error,
    string? Detalle,
    DateTimeOffset FechaConsulta,
    string? FechaLanzamiento = null)   // campo AGREGADO (aaaa-mm-dd) tomado de la página; sirve para verificar el REPO
{
    public static ResultadoMetacritic Exito(int score, string plataforma, bool verificada, string enlace, string? fechaLanzamiento = null) =>
        new(true, score, plataforma, verificada, enlace, null, null, DateTimeOffset.UtcNow, fechaLanzamiento);

    public static ResultadoMetacritic Fallo(string tipoError, string? detalle = null) =>
        new(false, null, null, null, null, tipoError, detalle, DateTimeOffset.UtcNow);
}

/// <summary>
/// { ok, horas_completo, fecha_consulta }. horas_completo = "Completionist" de HLTB (100 %).
/// Se agregan (solo se AGREGAN campos, nunca se quitan ni renombran): horas_principal (Main Story), horas_extra (Main + Extras),
/// horas_todos_los_estilos (comp_all) y los extras que trae la página pública /game/ID de HowLongToBeat
/// (desarrollador, plataformas, año, calificación de usuarios, cuántos lo completaron / lo juegan / lo tienen pendiente, imagen).
/// Todo lo que HLTB no publique para ese juego queda en null y la web lo muestra como "N/D".
/// fuente = "HowLongToBeat" (atribución obligatoria en el sitio).
/// </summary>
public record ResultadoHltb(
    bool Ok,
    double? HorasCompleto,
    double? HorasPrincipal,
    double? HorasExtra,
    string? Enlace,
    string? Error,
    string? Detalle,
    DateTimeOffset FechaConsulta,
    double? HorasTodosLosEstilos = null,
    string? NombreHltb = null,
    string? Desarrollador = null,
    string? Plataformas = null,
    int? AnioLanzamiento = null,
    int? CalificacionUsuarios = null,
    int? NumResenas = null,
    int? Completaron = null,
    int? Jugando = null,
    int? Pendiente = null,
    int? Abandonaron = null,
    string? ImagenUrl = null,
    string? Fuente = null)
{
    public static ResultadoHltb Exito(double? completo, double? principal, double? extra, string? enlace) =>
        new(true, completo, principal, extra, enlace, null, null, DateTimeOffset.UtcNow, Fuente: "HowLongToBeat");

    public static ResultadoHltb Fallo(string tipoError, string? detalle = null) =>
        new(false, null, null, null, null, tipoError, detalle, DateTimeOffset.UtcNow);
}

/// <summary>Opciones JSON del contrato: snake_case y sin campos nulos.</summary>
public static class Serializacion
{
    public static readonly JsonSerializerOptions Opciones = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string AJson<T>(T valor) => JsonSerializer.Serialize(valor, Opciones);
}
