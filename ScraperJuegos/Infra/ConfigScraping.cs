namespace ScraperJuegos;

/// <summary>Parámetros de robustez y cortesía. Valores por defecto prudentes; se pueden cambiar sin tocar los scrapers.</summary>
public record ConfigScraping
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(15);
    /// <summary>Reintentos ADICIONALES al primer intento (3 = hasta 4 solicitudes en total).</summary>
    public int MaxReintentos { get; init; } = 3;
    /// <summary>Espera creciente: base * 2^(intento-1) + jitter. 1s, 2s, 4s...</summary>
    public TimeSpan EsperaBase { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan EsperaMaxima { get; init; } = TimeSpan.FromSeconds(30);
    public bool RespetarRobots { get; init; } = true;

    /// <summary>User-Agent honesto: dice quiénes somos y cómo contactarnos. Configure SCRAPER_CONTACTO.</summary>
    public string UserAgent { get; init; } = SoloAscii(
        $"ScraperJuegosTEC/1.0 (proyecto academico IC3101 TEC San Carlos; contacto: {Environment.GetEnvironmentVariable("SCRAPER_CONTACTO") ?? "CONFIGURAR-SCRAPER_CONTACTO"})");

    /// <summary>Los headers HTTP solo admiten ASCII: quita tildes y caracteres no ASCII (ej. "José" -> "Jose").</summary>
    private static string SoloAscii(string s) =>
        new string(s.Normalize(System.Text.NormalizationForm.FormD).Where(c => c < 128).ToArray());

    /// <summary>
    /// Nintendo eShop busca con un índice de Algolia usando una clave PÚBLICA de solo búsqueda que el propio nintendo.com entrega a todo visitante
    /// (no es una clave privada del equipo). Puede ROTAR: si Nintendo la cambia, el scraper devuelve "bloqueo" (403) sin romper nada;
    /// se actualiza con la variable de entorno NINTENDO_ALGOLIA_KEY, sin recompilar. Lo mismo para NINTENDO_ALGOLIA_APP_ID y NINTENDO_ALGOLIA_INDEX.
    /// </summary>
    public string NintendoAlgoliaAppId { get; init; } = Environment.GetEnvironmentVariable("NINTENDO_ALGOLIA_APP_ID") ?? "U3B6GR4UA3";
    public string NintendoAlgoliaKey { get; init; } = Environment.GetEnvironmentVariable("NINTENDO_ALGOLIA_KEY") ?? "a29c6927638bfd8cee23993e51e721c9";
    public string NintendoAlgoliaIndex { get; init; } = Environment.GetEnvironmentVariable("NINTENDO_ALGOLIA_INDEX") ?? "store_game_en_us";

    /// <summary>Token con el que se busca nuestro grupo en robots.txt (antes de caer al grupo "*").</summary>
    public string TokenRobots { get; init; } = "ScraperJuegosTEC";
}

/// <summary>Error controlado: lo lanzan ClienteHttp y los scrapers, y se convierte en un resultado ok:false.</summary>
public sealed class ScraperException : Exception
{
    public string Tipo { get; }
    public ScraperException(string tipo, string detalle) : base(detalle) => Tipo = tipo;
}

/// <summary>Datos para el log: de qué juego/fuente es una solicitud.</summary>
public readonly record struct ContextoLog(string Juego, string Plataforma, string Fuente);
