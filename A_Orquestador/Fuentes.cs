using ScraperJuegos;

// Las fuentes que usa el orquestador. Mock o real: el resto del código no cambia.
public sealed record Fuentes(ITiendaScraper[] Tiendas, IMetacriticScraper Metacritic, IHltbScraper Hltb)
{
    public static Fuentes Mock(ILimitadorSolicitudes lim) => new(
        new ITiendaScraper[] { new MockTienda("TiendaA", lim), new MockTienda("TiendaB", lim) },
        new MockMetacritic(lim), new MockHltb(lim));

    // Scrapers reales de B. El limitador de A va dentro del ClienteHttp.
    public static Fuentes Real(ILimitadorSolicitudes lim, ILogFallos logScrapers)
    {
        var http = ClienteHttp.Crear(new ConfigScraping(), lim, logScrapers);
        return new Fuentes(TiendasDisponibles.Crear(http), new MetacriticScraper(http), new HltbScraper(http));
    }

    // Red de seguridad: si una fuente lanzara una excepción, se convierte en fallo controlado.
    public static async Task<T> Seguro<T>(Func<Task<T>> consulta, Func<string, string?, T> fallo)
    {
        try { return await consulta(); }
        catch (Exception ex) { return fallo(TipoError.FormatoCambiado, $"{ex.GetType().Name}: {ex.Message}"); }
    }
}
