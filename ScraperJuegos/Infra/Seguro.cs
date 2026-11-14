namespace ScraperJuegos;

/// <summary>
/// Envoltorio que garantiza "nunca detiene el programa": convierte cualquier excepción en un resultado de error
/// controlado y lo registra en el log. Solo deja pasar la cancelación pedida por el orquestador.
/// </summary>
internal static class Seguro
{
    public static async Task<TRes> EjecutarAsync<TRes>(
        Func<Task<TRes>> operacion,
        Func<string, string?, TRes> fallo,
        ILogFallos log, Juego juego, string fuente, CancellationToken ct)
    {
        try
        {
            return await operacion().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (ScraperException ex)
        {
            log.Registrar(juego.Nombre, juego.Plataforma, fuente, ex.Tipo, ex.Message);
            return fallo(ex.Tipo, ex.Message);
        }
        catch (Exception ex)
        {
            // JsonException, KeyNotFound, NullReference...: casi siempre significa que la respuesta cambió de forma.
            string detalle = $"{ex.GetType().Name}: {ex.Message}";
            log.Registrar(juego.Nombre, juego.Plataforma, fuente, TipoError.FormatoCambiado, detalle);
            return fallo(TipoError.FormatoCambiado, detalle);
        }
    }
}
