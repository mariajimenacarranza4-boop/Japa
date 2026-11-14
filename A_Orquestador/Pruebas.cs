using System.Diagnostics;
using ScraperJuegos;

// Pruebas de concurrencia (sin red). Evidencia para "¿cómo evitan condiciones de carrera?".
// Se corren con:  dotnet run -- pruebas
public static class Pruebas
{
    public static async Task<int> Correr()
    {
        int fallos = 0;
        void Verificar(string nombre, bool ok, string detalle = "")
        {
            Console.WriteLine($"[{(ok ? "OK   " : "FALLA")}] {nombre} {detalle}");
            if (!ok) fallos++;
        }

        // 1) Límite por sitio: 30 tareas piden el mismo dominio a la vez.
        //    Nunca debe haber más de 3 dentro, y las entradas deben quedar separadas por la pausa (100 ms).
        var lim = new LimitadorA(maxPorSitio: 3, pausaMs: 100);
        int actual = 0, maximo = 0;
        var reloj = Stopwatch.StartNew();
        await Task.WhenAll(Enumerable.Range(0, 30).Select(async _ =>
        {
            using (await lim.EntrarAsync("sitio.test"))
            {
                int n = Interlocked.Increment(ref actual);
                int m;
                while (n > (m = Volatile.Read(ref maximo)) &&
                       Interlocked.CompareExchange(ref maximo, n, m) != m) { }
                await Task.Delay(50);
                Interlocked.Decrement(ref actual);
            }
        }));
        long ms = reloj.ElapsedMilliseconds;
        Verificar("Límite por sitio: nunca más de 3 simultáneas", maximo <= 3, $"(máximo observado: {maximo})");
        Verificar("Pausa entre solicitudes respetada", ms >= 2750, $"(30 solicitudes tardaron {ms} ms; mínimo esperado ~2900)");

        // 2) Nivel 1 con 8 trabajadores y los 3 niveles: sin pérdidas, sin duplicados, mismo orden del REPO.
        var juegos = Enumerable.Range(1, 40).Select(i => new JuegoAgrupado($"Juego {i}",
            new List<Juego> { new Juego(i, $"Juego {i}", "PS5", DateTime.Now, 59.99m) })).ToList();
        var res = await Paralelo.Ejecutar(juegos, Fuentes.Mock(new LimitadorA(5, 10)), 8, true, true);
        Verificar("40 juegos -> 40 registros, ninguno nulo", res.Count == 40 && res.All(r => r != null));
        Verificar("Mismo orden del REPO y sin duplicados",
            res.Select(r => r.Id).SequenceEqual(juegos.Select(g => g.Filas[0].Id.ToString())));

        // 3) Log de fallos compartido: 200 escrituras simultáneas, todas las líneas completas.
        string tmp = Path.GetTempFileName();
        using (var log = new LogFallosArchivo(tmp))
            Parallel.For(0, 200, i => log.Registrar($"j{i}", "PS5", "fuente", "timeout", "x"));
        var lineas = File.ReadAllLines(tmp);
        Verificar("Log de fallos: 200 líneas completas, sin mezclarse",
            lineas.Length == 200 && lineas.All(l => l.Split(';').Length == 6));
        File.Delete(tmp);

        Console.WriteLine(fallos == 0 ? "\nTodas las pruebas pasaron." : $"\n{fallos} prueba(s) fallaron.");
        return fallos;
    }
}
