using ScraperJuegos;

// "Apagar una fuente a propósito" para la defensa: esa fuente devuelve un fallo controlado
// (timeout) sin tocar la red. Se activa con la variable de entorno FUENTES_APAGADAS,
// por ejemplo:  $env:FUENTES_APAGADAS="Steam,hltb"   (nombres de tienda, "metacritic" o "hltb").
// Sin la variable, no cambia nada.
public static class FuentesApagadas
{
    private const string Detalle = "apagada a propósito (demo)";

    public static Fuentes ApagarSegunEntorno(this Fuentes f)
    {
        var nombres = new HashSet<string>(
            (Environment.GetEnvironmentVariable("FUENTES_APAGADAS") ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.OrdinalIgnoreCase);
        if (nombres.Count == 0) return f;

        Console.WriteLine($"** Fuentes apagadas a propósito: {string.Join(", ", nombres)} **");
        return new Fuentes(
            f.Tiendas.Select(t => nombres.Contains(t.Tienda) ? (ITiendaScraper)new TiendaApagada(t) : t).ToArray(),
            nombres.Contains("metacritic") ? new MetacriticApagado() : f.Metacritic,
            nombres.Contains("hltb") ? new HltbApagado() : f.Hltb);
    }

    private sealed class TiendaApagada : ITiendaScraper
    {
        private readonly ITiendaScraper _real;
        public TiendaApagada(ITiendaScraper real) => _real = real;
        public string Tienda => _real.Tienda;
        public string Dominio => _real.Dominio;
        public bool SoportaPlataforma(string plataforma) => _real.SoportaPlataforma(plataforma);
        public Task<ResultadoTienda> ConsultarAsync(Juego juego, CancellationToken ct = default) =>
            Task.FromResult(ResultadoTienda.Fallo(Tienda, TipoError.Timeout, Detalle));
    }

    private sealed class MetacriticApagado : IMetacriticScraper
    {
        public Task<ResultadoMetacritic> ConsultarAsync(Juego juego, CancellationToken ct = default) =>
            Task.FromResult(ResultadoMetacritic.Fallo(TipoError.Timeout, Detalle));
    }

    private sealed class HltbApagado : IHltbScraper
    {
        public Task<ResultadoHltb> ConsultarAsync(Juego juego, CancellationToken ct = default) =>
            Task.FromResult(ResultadoHltb.Fallo(TipoError.Timeout, Detalle));
    }
}
