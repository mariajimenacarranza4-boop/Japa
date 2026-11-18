namespace ScraperJuegos;

/// <summary>
/// ÚNICO lugar donde se listan las tiendas. Agregar una tienda = escribir su clase (ITiendaScraper) y añadirla aquí
/// (y su nombre en Catalogos.Tiendas; una prueba automática avisa si se olvidan de sincronizarlos).
/// El resto del programa (probar, resumen del REPO) recorre esta lista, así que no hay que tocar nada más.
/// </summary>
public static class TiendasDisponibles
{
    /// <summary>
    /// Crea las tiendas. Se pueden APAGAR sin recompilar con la variable de entorno TIENDAS_DESACTIVADAS
    /// (nombres separados por coma, ej. "Epic Games Store,Eneba"): una tienda que se sabe caída
    /// (403 permanente, página que ya no trae datos) deja de gastar solicitudes, tiempo y líneas de log, y no ensucia las mediciones.
    /// </summary>
    public static ITiendaScraper[] Crear(ClienteHttp http) => Crear(http, NombresDesactivados());

    public static ITiendaScraper[] Crear(ClienteHttp http, IEnumerable<string>? desactivadas)
    {
        var apagadas = new HashSet<string>((desactivadas ?? Array.Empty<string>()).Select(n => n.Trim()).Where(n => n.Length > 0),
                                           StringComparer.OrdinalIgnoreCase);
        return new ITiendaScraper[]
        {
            new SteamScraper(http),
            // Epic: store.epicgames.com responde 403 y no se evade; los precios de Epic se leen de la API pública de CheapShark.
            // (EpicGamesScraper, el acceso directo, se conserva en el código como evidencia, pero no se registra.)
            new EpicCheapSharkScraper(http),
            new NintendoScraper(http),
            new EnebaScraper(http),
        }.Where(t => !apagadas.Contains(t.Tienda)).ToArray();
    }

    public static string[] NombresDesactivados() =>
        (Environment.GetEnvironmentVariable("TIENDAS_DESACTIVADAS") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Consulta una tienda y NUNCA lanza (ni siquiera si un scraper tuviera un error de programación). Solo deja pasar la cancelación.</summary>
    public static async Task<ResultadoTienda> ConsultarSeguroAsync(ITiendaScraper tienda, Juego juego, CancellationToken ct = default)
    {
        try { return await tienda.ConsultarAsync(juego, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            return ResultadoTienda.Fallo(tienda.Tienda, TipoError.FormatoCambiado, $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
