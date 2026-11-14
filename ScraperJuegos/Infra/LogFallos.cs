using System.Collections.Concurrent;

namespace ScraperJuegos;

/// <summary>Registro de fallos (juego, plataforma, fuente, tipo de error, hora). Seguro entre hilos.</summary>
public interface ILogFallos
{
    void Registrar(string juego, string plataforma, string fuente, string tipo, string? detalle);
}

/// <summary>
/// Escribe una línea por evento en un archivo (separado por ;) y lleva contadores por tipo.
/// Recurso compartido protegido: un lock alrededor de la escritura al archivo; contadores en ConcurrentDictionary.
/// Formato: fecha_utc;juego;plataforma;fuente;tipo;detalle
/// </summary>
public sealed class LogFallosArchivo : ILogFallos, IDisposable
{
    private readonly object _candado = new();
    private readonly StreamWriter? _escritor;
    private readonly bool _consola;
    public ConcurrentDictionary<string, int> ContadoresPorTipo { get; } = new();

    public LogFallosArchivo(string? rutaArchivo, bool escribirEnConsola = false)
    {
        _consola = escribirEnConsola;
        if (!string.IsNullOrWhiteSpace(rutaArchivo))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(rutaArchivo))!);
            _escritor = new StreamWriter(rutaArchivo, append: true) { AutoFlush = true };
        }
    }

    public void Registrar(string juego, string plataforma, string fuente, string tipo, string? detalle)
    {
        ContadoresPorTipo.AddOrUpdate(tipo, 1, (_, n) => n + 1);
        string linea = string.Join(';',
            DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            Limpiar(juego), Limpiar(plataforma), Limpiar(fuente), tipo, Limpiar(detalle ?? ""));
        lock (_candado)
        {
            _escritor?.WriteLine(linea);
            if (_consola) Console.WriteLine("  [LOG] " + linea);
        }
    }

    private static string Limpiar(string s) => s.Replace(';', ',').Replace('\r', ' ').Replace('\n', ' ');
    public void Dispose() { lock (_candado) _escritor?.Dispose(); }
}

/// <summary>No registra nada (para pruebas).</summary>
public sealed class LogNulo : ILogFallos
{
    public void Registrar(string juego, string plataforma, string fuente, string tipo, string? detalle) { }
}
