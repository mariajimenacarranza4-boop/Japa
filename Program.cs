using System.Diagnostics;
using System.Globalization;
using System.Text;
using ScraperJuegos;

// Pruebas de concurrencia:  dotnet run -- pruebas
if (args.Length > 0 && args[0].Equals("pruebas", StringComparison.OrdinalIgnoreCase))
{
    Environment.ExitCode = await Pruebas.Correr();
    return;
}

// Limpia un catálogo ya generado (quita precios sospechosos de Eneba):  dotnet run -- limpiar [catalogo.json]
if (args.Length > 0 && args[0].Equals("limpiar", StringComparison.OrdinalIgnoreCase))
{
    string rutaLimpiar = args.Length > 1 ? args[1] : "catalogo.json";
    if (!File.Exists(rutaLimpiar)) { Console.WriteLine($"No existe {rutaLimpiar}"); return; }
    Catalogo.Limpiar(rutaLimpiar);
    return;
}

var argReps = args.FirstOrDefault(a => a.StartsWith("reps=", StringComparison.OrdinalIgnoreCase));
int REPETICIONES = argReps != null && int.TryParse(argReps[5..], out var rr) && rr > 0 ? rr : 3;

bool real = args.Length > 0 && args[0].Equals("real", StringComparison.OrdinalIgnoreCase);
// Ruta del CSV: argumento, o ..\ScraperJuegos\Data (si se corre desde esta carpeta), o relativa al .exe (funciona desde cualquier carpeta).
string rutaCsv = args.Length > 1 && !args[1].Contains('=') ? args[1] : Path.Combine("..", "ScraperJuegos", "Data", "juegos.csv");
if (!File.Exists(rutaCsv))
{
    string alt = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "ScraperJuegos", "Data", "juegos.csv"));
    if (File.Exists(alt)) rutaCsv = alt;
}
// Sin número: "real" procesa TODOS los juegos del REPO (actualización automática); "medir" usa 3 por defecto.
bool medirArg = args.Contains("medir", StringComparer.OrdinalIgnoreCase);
int nReal = args.Length > 2 && int.TryParse(args[2], out var nn) ? nn : (medirArg ? 3 : int.MaxValue);
string? salida = args.FirstOrDefault(a => a.StartsWith("salida=", StringComparison.OrdinalIgnoreCase))?[7..];
string archivoCatalogo = salida ?? (real ? "catalogo.json" : "catalogo_mock.json");
if (!string.IsNullOrWhiteSpace(Path.GetDirectoryName(archivoCatalogo)))
    Directory.CreateDirectory(Path.GetDirectoryName(archivoCatalogo)!);
bool medir = args.Contains("medir", StringComparer.OrdinalIgnoreCase);
// resultados=archivo.csv evita pisar mediciones anteriores (ej. la de 40 juegos al medir los 249).
string archivoResultados = args.FirstOrDefault(a => a.StartsWith("resultados=", StringComparison.OrdinalIgnoreCase))?[11..]
                           ?? (real ? "resultados_real.csv" : "resultados.csv");
int trabajadores = 2;
var argTrab = args.FirstOrDefault(a => a.StartsWith("trab=", StringComparison.OrdinalIgnoreCase));
if (argTrab != null && int.TryParse(argTrab[5..], out var tt) && tt > 0) trabajadores = tt;
else if (real && !medir) trabajadores = Environment.ProcessorCount;


var logFallos = new LogFallosArchivo("log_fallos.txt");   // un solo log para A y B
Log.Destino = logFallos;
Func<Fuentes> fabrica = real
    ? () => Fuentes.Real(LimitadorReal(), logFallos).ApagarSegunEntorno()
    : () => Fuentes.Mock(new LimitadorA(maxPorSitio: 3, pausaMs: 100)).ApagarSegunEntorno();   // limitador NUEVO en cada corrida

List<JuegoAgrupado> juegos;
int totalRepo = -1;   // cuántos juegos tiene el REPO (solo en modo real)
if (real)
{
    var carga = new RepoCsv(rutaCsv).Cargar();
    var grupos = carga.AgruparPorJuego();
    Console.WriteLine($"REPO: {carga.Juegos.Count} filas | {grupos.Count} juegos | {carga.Errores.Count} errores de carga");
    totalRepo = grupos.Count;
    juegos = grupos.Take(nReal).ToList();
}
else
{
    juegos = Enumerable.Range(1, 10).Select(i =>
    {
        string nombre = $"Juego Falso {i}";
        var filas = new List<Juego> { new Juego(i * 10, nombre, "PS5", DateTime.Now.AddYears(-1), 59.99m) };
        if (i % 2 == 0) filas.Add(new Juego(i * 10 + 1, nombre, "PC", DateTime.Now.AddYears(-1), 59.99m));
        return new JuegoAgrupado(nombre, filas);
    }).ToList();
}

// Re-medición de filas sueltas (sin rehacer toda la matriz):
//   dotnet run -- real <ruta> 40 medir reps=3 config=1,2 modo=123
// Usa el tiempo secuencial ya guardado en el CSV y reemplaza solo esas filas (modo + trabajadores).
var argConfig = args.FirstOrDefault(a => a.StartsWith("config=", StringComparison.OrdinalIgnoreCase));
bool remedir = argConfig != null;

if (!remedir)
{
    // --- DEMO con log en vivo ---
    int nDemo = real ? (medir ? 3 : juegos.Count) : 4;
    Log.Verboso = true;
    Console.WriteLine($"=== DEMO: {nDemo} juegos, {trabajadores} trabajadores , 3 niveles ({(real ? "REAL" : "MOCK")}) ===");
    var demo = await Paralelo.Ejecutar(juegos.Take(nDemo).ToList(), fabrica(), trabajadores, true, true);
    Log.Verboso = false;
    Console.WriteLine("=== FIN DEMO ===\n");

    // Si se procesó el REPO completo, los juegos que ya no están en el REPO se sacan del catálogo.
    var idsVigentes = (real && nDemo >= totalRepo) ? juegos.Select(g => g.Filas[0].Id.ToString()).ToHashSet() : null;
    var finales = Catalogo.Actualizar(archivoCatalogo, demo, idsVigentes);   // conserva el último dato bueno si algo falló
    foreach (var r in finales)
        Console.WriteLine($"{r.Nombre,-20} mejor: {r.MejorPrecio?.ToString() ?? "N/D"} en {r.MejorTienda ?? "-"} | -{r.DescuentoPct}% | MC {r.Metacritic?.Score.ToString() ?? "N/D"} | HLTB {r.HltbHoras?.ToString() ?? "N/D"} | {string.Join(", ", r.EstadoFuentes.Select(kv => $"{kv.Key}:{kv.Value}"))}");

    Console.WriteLine($"\nGuardado {archivoCatalogo}");
    if (real && !medir) return;   // las mediciones reales solo se hacen con "medir"
}

if (remedir)
{
    var configs = argConfig![7..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(x => int.TryParse(x, out var v) ? v : 0).Where(v => v > 0).Distinct().ToList();
    string modoArg = args.FirstOrDefault(a => a.StartsWith("modo=", StringComparison.OrdinalIgnoreCase))?[5..] ?? "123";
    var elegidos = new (string nombre, bool n2, bool n3)[]
    {
        ("Solo nivel 1", false, false), ("Niveles 1+2", true, false), ("Niveles 1+2+3", true, true),
    }.Where(m => modoArg == "123" ? m.n3 : modoArg == "12" ? (m.n2 && !m.n3) : modoArg == "1" ? (!m.n2) : false).ToList();

    if (!File.Exists(archivoResultados)) { Console.WriteLine($"No existe {archivoResultados}: haga primero la medición completa."); return; }
    var previas = File.ReadAllLines(archivoResultados).ToList();
    var filaSec = previas.Skip(1).FirstOrDefault(l => l.StartsWith("Secuencial,", StringComparison.OrdinalIgnoreCase));
    if (filaSec == null) { Console.WriteLine("El CSV no tiene la fila Secuencial."); return; }
    double tSecGuardado = double.Parse(filaSec.Split(',')[2], CultureInfo.InvariantCulture);
    Console.WriteLine($"Re-medición: {elegidos.Count} modo(s) x trabajadores [{string.Join(",", configs)}] | juegos: {juegos.Count} | reps: {REPETICIONES} | secuencial guardado: {tSecGuardado:F1} s");

    var swr = new Stopwatch();
    foreach (var (nombre, n2, n3) in elegidos)
        foreach (int n in configs)
        {
            if (n > juegos.Count) { Console.WriteLine($"Se omite {nombre} con {n}: más trabajadores que juegos."); continue; }
            var ts = new List<double>();
            for (int i = 0; i < REPETICIONES; i++)
            {
                var f = fabrica();
                swr.Restart();
                await Paralelo.Ejecutar(juegos, f, n, n2, n3);
                swr.Stop();
                ts.Add(swr.Elapsed.TotalSeconds);
                Console.WriteLine($"  {nombre} n={n} repetición {i + 1}: {ts[^1]:F1} s");
            }
            double t = ts.Average(), sp = tSecGuardado / t, ef = sp / n * 100;
            string nueva = string.Format(CultureInfo.InvariantCulture, "{0},{1},{2:F2},{3:F3},{4:F1},{5}", nombre, n, t, sp, ef, (!n2 && !n3) ? "si" : "no");
            string clave = $"{nombre},{n},";
            int pos = previas.FindIndex(1, l => l.StartsWith(clave, StringComparison.OrdinalIgnoreCase));
            if (pos >= 0) previas[pos] = nueva; else previas.Add(nueva);
            File.WriteAllLines(archivoResultados, previas.Where(l => l.Length > 0));   // guarda tras cada fila
            Console.WriteLine($"{nombre,-15}{n,6}{t,12:F1}{sp,10:F2}{ef,9:F1}%   (reemplazada en {archivoResultados})");
        }
    return;
}

// --- Mediciones (log apagado) ---
var sw = new Stopwatch();
int nucleos = Environment.ProcessorCount;
// Más trabajadores que juegos no aporta nada (quedarían ociosos): se omiten esas configuraciones.
int maximo = Math.Min(nucleos, juegos.Count);
var configuraciones = new[] { 1, 2, 4, maximo }.Where(x => x <= juegos.Count).Distinct().OrderBy(x => x).ToList();

var tiemposSec = new List<double>();
for (int i = 0; i < REPETICIONES; i++)
{
    var f = fabrica();
    sw.Restart();
    await Secuencial.Ejecutar(juegos, f);
    sw.Stop();
    tiemposSec.Add(sw.Elapsed.TotalSeconds);
}
double tSec = tiemposSec.Average();

var modos = new (string nombre, bool n2, bool n3)[]
{
    ("Solo nivel 1", false, false),
    ("Niveles 1+2", true, false),
    ("Niveles 1+2+3", true, true),
};

var csv = new StringBuilder();
csv.AppendLine("modo,trabajadores,tiempo_s,speedup,eficiencia_pct,eficiencia_interpretable");
csv.AppendLine(string.Format(CultureInfo.InvariantCulture, "Secuencial,1,{0:F2},1.000,100.0,si", tSec));
File.WriteAllText(archivoResultados, csv.ToString());

Console.WriteLine($"\nNúcleos lógicos: {nucleos} | Juegos: {juegos.Count} | Repeticiones: {REPETICIONES}");
Console.WriteLine($"Tiempo secuencial (promedio): {tSec:F1} s\n");
Console.WriteLine($"{"Modo",-15}{"Trab.",6}{"Tiempo(s)",12}{"Speedup",10}{"Efic.",9}");
Console.WriteLine(new string('-', 52));

foreach (var (nombre, n2, n3) in modos)
{
    // En real, el análisis por niveles se mide solo con el máximo; la matriz completa, solo con los 3 niveles.
    foreach (int n in (!real || (n2 && n3)) ? configuraciones : new List<int> { maximo })
    {
        var ts = new List<double>();
        for (int i = 0; i < REPETICIONES; i++)
        {
            var f = fabrica();
            sw.Restart();
            await Paralelo.Ejecutar(juegos, f, n, n2, n3);
            sw.Stop();
            ts.Add(sw.Elapsed.TotalSeconds);
        }
        double t = ts.Average();
        double speedup = tSec / t;
        double efic = speedup / n * 100;   // eficiencia = speedup / n.º de trabajadores
        // Con los niveles 2 y 3 cada trabajador lanza varias consultas de red a la vez (async, E/S),
        // así que hay más concurrencia que "n": la eficiencia puede pasar de 100 % y NO es comparable con núcleos.
        bool interpretable = !n2 && !n3;

        Console.WriteLine($"{nombre,-15}{n,6}{t,12:F1}{speedup,10:F2}{efic.ToString("F1", CultureInfo.InvariantCulture) + "%" + (interpretable ? "" : "*"),9}");
        csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "{0},{1},{2:F2},{3:F3},{4:F1},{5}", nombre, n, t, speedup, efic, interpretable ? "si" : "no"));
        File.WriteAllText(archivoResultados, csv.ToString());   // guarda tras cada fila: si se cae, no se pierde lo anterior
    }
    Console.WriteLine();
}

Console.WriteLine("Nota: eficiencia = speedup / n.º de trabajadores.");
Console.WriteLine("* Con los niveles 2 y 3 hay concurrencia extra por trabajador (E/S de red); la eficiencia puede superar 100 % y no mide núcleos.");
Console.WriteLine($"Guardado en {archivoResultados}");

// Límite por dominio de la corrida real: 2 simultáneas y 1 s entre solicitudes para todos los sitios, salvo Eneba (más sensible:
// aplica límites de uso), que va de 1 en 1 con 3 s de pausa. Se ajusta sin recompilar con ENEBA_PAUSA_MS.
static LimitadorA LimitadorReal()
{
    int pausaEneba = int.TryParse(Environment.GetEnvironmentVariable("ENEBA_PAUSA_MS"), out var p) && p >= 0 ? p : 3000;
    return new LimitadorA(maxPorSitio: 2, pausaMs: 1000,
        porSitio: new Dictionary<string, (int Max, int PausaMs)> { ["www.eneba.com"] = (1, pausaEneba) });
}
