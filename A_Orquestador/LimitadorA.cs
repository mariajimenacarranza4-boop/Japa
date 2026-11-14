using System.Collections.Concurrent;
using System.Diagnostics;
using ScraperJuegos;

// Límite de concurrencia por dominio de A. Implementa la interfaz de B (ILimitadorSolicitudes),
// así que se le INYECTA al ClienteHttp de B: los scrapers reales respetan ESTE límite.
//
// Recursos compartidos y cómo se protegen:
//  - Diccionario de sitios      -> ConcurrentDictionary (GetOrAdd es atómico)
//  - Máx. simultáneas por sitio -> SemaphoreSlim (uno por dominio)
//  - Pausa entre solicitudes    -> lock corto sobre ProximoInicioMs de ese dominio
public sealed class LimitadorA : ILimitadorSolicitudes
{
    private sealed class Sitio
    {
        public Sitio(int max, long pausaMs) { Semaforo = new SemaphoreSlim(max, max); PausaMs = pausaMs; }
        public SemaphoreSlim Semaforo { get; }
        public readonly object Candado = new();
        public long ProximoInicioMs;   // protegido por Candado
        public long PausaMs;           // protegido por Candado
    }

    private sealed class Liberador : IDisposable
    {
        private SemaphoreSlim? _s;
        public Liberador(SemaphoreSlim s) => _s = s;
        public void Dispose() => Interlocked.Exchange(ref _s, null)?.Release();
    }

    private readonly ConcurrentDictionary<string, Sitio> _sitios = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stopwatch _reloj = Stopwatch.StartNew();

    public int MaxPorSitio { get; }   // CONFIGURABLE: peticiones simultáneas por sitio
    public int PausaMs { get; }       // CONFIGURABLE: separación mínima entre inicios de peticiones

    public LimitadorA(int maxPorSitio = 3, int pausaMs = 100)
    {
        MaxPorSitio = Math.Max(1, maxPorSitio);
        PausaMs = Math.Max(0, pausaMs);
    }

    private Sitio Obtener(string dominio) =>
        _sitios.GetOrAdd(dominio, _ => new Sitio(MaxPorSitio, PausaMs));

    public async Task<IDisposable> EntrarAsync(string dominio, CancellationToken ct = default)
    {
        var s = Obtener(dominio);
        await s.Semaforo.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            long espera;
            lock (s.Candado)
            {
                long ahora = _reloj.ElapsedMilliseconds;
                long inicio = Math.Max(ahora, s.ProximoInicioMs);
                s.ProximoInicioMs = inicio + s.PausaMs;
                espera = inicio - ahora;
            }
            if (espera > 0) await Task.Delay((int)espera, ct).ConfigureAwait(false);
        }
        catch
        {
            s.Semaforo.Release();
            throw;
        }
        return new Liberador(s.Semaforo);
    }

    // B puede exigir más pausa (Crawl-delay de robots.txt). Nunca la baja.
    public void ExigirPausaMinima(string dominio, TimeSpan pausa)
    {
        var s = Obtener(dominio);
        lock (s.Candado) s.PausaMs = Math.Max(s.PausaMs, (long)pausa.TotalMilliseconds);
    }
}
