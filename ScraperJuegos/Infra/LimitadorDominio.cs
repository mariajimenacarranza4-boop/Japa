using System.Collections.Concurrent;
using System.Diagnostics;

namespace ScraperJuegos;

/// <summary>
/// Límite por dominio que los scrapers RESPETAN. A puede inyectar el suyo (misma interfaz);
/// mientras tanto este sirve por defecto.
/// </summary>
public interface ILimitadorSolicitudes
{
    /// <summary>Espera el turno para pedir a un dominio. Se libera con Dispose() al terminar la solicitud.</summary>
    Task<IDisposable> EntrarAsync(string dominio, CancellationToken ct = default);
    /// <summary>Sube la pausa mínima de un dominio (ej. por Crawl-delay de robots.txt). Nunca la baja.</summary>
    void ExigirPausaMinima(string dominio, TimeSpan pausa);
}

/// <summary>
/// Semáforo por dominio (máx. solicitudes simultáneas) + pausa mínima entre el inicio de dos solicitudes.
/// Recurso compartido protegido: el SemaphoreSlim y un lock corto sobre la "próxima hora permitida".
/// </summary>
public sealed class LimitadorDominio : ILimitadorSolicitudes
{
    private sealed class Estado
    {
        public SemaphoreSlim Semaforo = null!;
        public readonly object Candado = new();
        public long ProximoInicioMs;          // protegido por Candado
        public long PausaMs;                  // protegido por Candado
    }

    private readonly ConcurrentDictionary<string, Estado> _dominios = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _maxConcurrencia;
    private readonly TimeSpan _pausaDefecto;
    private readonly Stopwatch _reloj = Stopwatch.StartNew();

    public LimitadorDominio(int maxConcurrenciaPorDominio = 2, TimeSpan? pausaMinima = null)
    {
        _maxConcurrencia = Math.Max(1, maxConcurrenciaPorDominio);
        _pausaDefecto = pausaMinima ?? TimeSpan.FromMilliseconds(1000);
    }

    private Estado Obtener(string dominio) => _dominios.GetOrAdd(dominio, _ => new Estado
    {
        Semaforo = new SemaphoreSlim(_maxConcurrencia, _maxConcurrencia),
        PausaMs = (long)_pausaDefecto.TotalMilliseconds
    });

    public async Task<IDisposable> EntrarAsync(string dominio, CancellationToken ct = default)
    {
        var e = Obtener(dominio);
        await e.Semaforo.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            long espera;
            lock (e.Candado)
            {
                long ahora = _reloj.ElapsedMilliseconds;
                long inicio = Math.Max(ahora, e.ProximoInicioMs);
                e.ProximoInicioMs = inicio + e.PausaMs;
                espera = inicio - ahora;
            }
            if (espera > 0) await Task.Delay((int)espera, ct).ConfigureAwait(false);
        }
        catch
        {
            e.Semaforo.Release();
            throw;
        }
        return new Liberador(e.Semaforo);
    }

    public void ExigirPausaMinima(string dominio, TimeSpan pausa)
    {
        var e = Obtener(dominio);
        lock (e.Candado) e.PausaMs = Math.Max(e.PausaMs, (long)pausa.TotalMilliseconds);
    }

    private sealed class Liberador : IDisposable
    {
        private SemaphoreSlim? _s;
        public Liberador(SemaphoreSlim s) => _s = s;
        public void Dispose() => Interlocked.Exchange(ref _s, null)?.Release();
    }
}
