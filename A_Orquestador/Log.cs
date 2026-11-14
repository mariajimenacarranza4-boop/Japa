using ScraperJuegos;

// Log compartido entre hilos. El lock evita que las líneas se mezclen.
public static class Log
{
    private static readonly object _lock = new();

    // true = imprime en consola (demo). false = silencioso (mediciones).
    public static bool Verboso { get; set; } = false;

    // Destino único de fallos (lo asigna Program). LogNulo = no escribe nada.
    public static ILogFallos Destino { get; set; } = new LogNulo();

    public static void Info(string juego, string nivel, string msg)
    {
        if (!Verboso) return;
        string linea = $"[{DateTime.Now:HH:mm:ss.fff}] Hilo {Environment.CurrentManagedThreadId,2} | {juego,-15} | {nivel,-7} | {msg}";
        lock (_lock) { Console.WriteLine(linea); }
    }

    // Los fallos SIEMPRE se guardan en archivo (lo pide el enunciado)
    public static void Fallo(string juego, string fuente, string? error)
    {
        Destino.Registrar(juego, "", fuente, error ?? "desconocido", "orquestador");
        if (Verboso) lock (_lock) { Console.WriteLine($"   !! FALLO | {juego} | {fuente} | {error}"); }
    }
}