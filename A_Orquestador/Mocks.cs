using ScraperJuegos;

// ============================================================
// MOCKS: fuentes falsas que cumplen las interfaces REALES de B.
// Sirven para probar/medir el esquema sin red. Se usan con: dotnet run (sin argumentos).
// ============================================================
internal static class Azar
{
    private static readonly Random _rnd = new();
    private static readonly object _lock = new(); // Random no es seguro entre hilos
    public static double Dbl() { lock (_lock) { return _rnd.NextDouble(); } }
    public static int Ent(int a, int b) { lock (_lock) { return _rnd.Next(a, b); } }
    public static bool Falla(double p) => Dbl() < p;
    public static Task Espera(CancellationToken ct) => Task.Delay(Ent(200, 800), ct); // simula red
}

public sealed class MockTienda : ITiendaScraper
{
    private readonly ILimitadorSolicitudes _lim;
    public MockTienda(string nombre, ILimitadorSolicitudes lim) { Tienda = nombre; _lim = lim; }
    public string Tienda { get; }
    public string Dominio => $"{Tienda.ToLowerInvariant()}.mock";
    public bool SoportaPlataforma(string plataforma) => true;

    public async Task<ResultadoTienda> ConsultarAsync(Juego juego, CancellationToken ct = default)
    {
        using (await _lim.EntrarAsync(Dominio, ct))
        {
            await Azar.Espera(ct);
            if (Azar.Falla(0.05)) return ResultadoTienda.Fallo(Tienda, TipoError.Timeout, "mock");
            decimal regular = juego.PrecioSugerido;
            decimal actual = Math.Round(regular * (decimal)(0.5 + Azar.Dbl() * 0.5), 2);
            return ResultadoTienda.Exito(Tienda, actual, regular, "digital", juego.Plataforma,
                $"https://ejemplo.com/{Tienda}/{juego.Id}", null);
        }
    }
}

public sealed class MockMetacritic : IMetacriticScraper
{
    private readonly ILimitadorSolicitudes _lim;
    public MockMetacritic(ILimitadorSolicitudes lim) => _lim = lim;

    public async Task<ResultadoMetacritic> ConsultarAsync(Juego juego, CancellationToken ct = default)
    {
        using (await _lim.EntrarAsync("metacritic.mock", ct))
        {
            await Azar.Espera(ct);
            if (Azar.Falla(0.05)) return ResultadoMetacritic.Fallo(TipoError.NoEncontrado, "mock");
            return ResultadoMetacritic.Exito(Azar.Ent(60, 96), juego.Plataforma, true, "https://ejemplo.com/metacritic");
        }
    }
}

public sealed class MockHltb : IHltbScraper
{
    private readonly ILimitadorSolicitudes _lim;
    public MockHltb(ILimitadorSolicitudes lim) => _lim = lim;

    public async Task<ResultadoHltb> ConsultarAsync(Juego juego, CancellationToken ct = default)
    {
        using (await _lim.EntrarAsync("hltb.mock", ct))
        {
            await Azar.Espera(ct);
            if (Azar.Falla(0.05)) return ResultadoHltb.Fallo(TipoError.Bloqueo, "mock");
            double h = Math.Round(5 + Azar.Dbl() * 60, 1);
            return ResultadoHltb.Exito(h, h * 0.5, h * 0.7, "https://ejemplo.com/hltb");
        }
    }
}
