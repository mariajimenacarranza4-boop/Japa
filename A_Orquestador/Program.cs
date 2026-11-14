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

var argReps = args.FirstOrDefault(a => a.StartsWith("reps=", StringComparison.OrdinalIgnoreCase));
int REPETICIONES = argReps != null && int.TryParse(argReps[5..], out var rr) && rr > 0 ? rr : 3;

bool real = args.Length > 0 && args[0].Equals("real", StringComparison.OrdinalIgnoreCase);
string rutaCsv = args.Length > 1 ? args[1] : Path.Combine("..", "ScraperJuegos", "Data", "juegos.csv");
int nReal = args.Length > 2 && int.TryParse(args[2], out var nn) ? nn : 3;
string? salida = args.FirstOrDefault(a => a.StartsWith("salida=", StringComparison.OrdinalIgnoreCase))?[7..];
string archivoCatalogo = salida ?? (real ? "catalogo.json" : "catalogo_mock.json");
if (!string.IsNullOrWhiteSpace(Path.GetDirectoryName(archivoCatalogo)))
    Directory.CreateDirectory(Path.GetDirectoryName(archivoCatalogo)!);
bool medir = args.Contains("medir", StringComparer.OrdinalIgnoreCase);
string archivoResultados = real ? "resultados_real.csv" : "resultados.csv";
int trabajadores = 2;
var argTrab = args.FirstOrDefault(a => a.StartsWith("trab=", StringComparison.OrdinalIgnoreCase));
if (argTrab != null && int.TryParse(argTrab[5..], out var tt) && tt > 0) trabajadores = tt;
else if (real && !medir) trabajadores = Environment.ProcessorCount;


var logFallos = new LogFallosArchivo("log_fallos.txt");   // un solo log para A y B
Log.Destino = logFallos;
Func<Fuentes> fabrica = real
    ? () => Fuentes.Real(new LimitadorA(maxPorSitio: 2, pausaMs: 1000), logFallos).ApagarSegunEntorno()
    : () => Fuentes.Mock(new LimitadorA(maxPorSitio: 3, pausaMs: 100)).ApagarSegunEntorno();   // limitador NUEVO en cada corrida

List<JuegoAgrupado> juegos;
if (real)
{
    var carga = new RepoCsv(rutaCsv).Cargar();
    var grupos = carga.AgruparPorJuego();
    Console.WriteLine($"REPO: {carga.Juegos.Count} filas | {grupos.Count} juegos | {carga.Errores.Count} errores de carga");
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

// --- DEMO con log en vivo ---
int nDemo = real ? (medir ? 3 : juegos.Count) : 4;
Log.Verboso = true;
Console.WriteLine($"=== DEMO: {nDemo} juegos, {trabajadores} trabajadores , 3 niveles ({(real ? "REAL" : "MOCK")}) ===");
var demo = await Paralelo.Ejecutar(juegos.Take(nDemo).ToList(), fabrica(), trabajadores, true, true);
Log.Verboso = false;
Console.WriteLine("=== FIN DEMO ===\n");

var finales = Catalogo.Actualizar(archivoCatalogo, demo);   // conserva el último dato bueno si algo falló
foreach (var r in finales)
    Console.WriteLine($"{r.Nombre,-20} mejor: {r.MejorPrecio?.ToString() ?? "N/D"} en {r.MejorTienda ?? "-"} | -{r.DescuentoPct}% | MC {r.Metacritic?.Score.ToString() ?? "N/D"} | HLTB {r.HltbHoras?.ToString() ?? "N/D"} | {string.Join(", ", r.EstadoFuentes.Select(kv => $"{kv.Key}:{kv.Value}"))}");

Console.WriteLine($"\nGuardado {archivoCatalogo}");
if (real && !medir) return;   // las mediciones reales solo se hacen con "medir"

// --- Mediciones (log apagado) ---
// "continuar": si la medición se cortó (apagón, suspensión, cierre), se vuelve a lanzar con la palabra continuar y
// se REUTILIZAN las filas ya guardadas en el CSV (mismo modo y n.º de trabajadores); solo se miden las que faltan.
bool continuar = args.Contains("continuar", StringComparer.OrdinalIgnoreCase);
var previas = new Dictionary<string, (double t, double desv)>(StringComparer.OrdinalIgnoreCase);
if (continuar && File.Exists(archivoResultados))
{
    foreach (var linea in File.ReadAllLines(archivoResultados).Skip(1))
    {
        var c = linea.Split(',');
        // Solo se reutiliza lo medido con los MISMOS juegos y repeticiones (columnas reps y juegos); filas viejas sin esas columnas, no.
        bool mismaMedicion = c.Length >= 8 && c[6] == REPETICIONES.ToString() && c[7] == juegos.Count.ToString();
        if (mismaMedicion && double.TryParse(c[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var tg))
            previas[$"{c[0]}|{c[1]}"] = (tg, c.Length >= 6 && double.TryParse(c[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var dg) ? dg : 0);
    }
    Console.WriteLine($"continuar: {previas.Count} mediciones ya guardadas en {archivoResultados} se reutilizan.");
}

var sw = new Stopwatch();
int maximo = Environment.ProcessorCount;
var configuraciones = new[] { 1, 2, 4, maximo }.Distinct().OrderBy(x => x).ToList();

static double Desviacion(List<double> xs)
{
    if (xs.Count < 2) return 0;
    double m = xs.Average();
    return Math.Sqrt(xs.Sum(x => (x - m) * (x - m)) / (xs.Count - 1));   // desviación estándar muestral
}

double tSec, dSec;
if (previas.TryGetValue("Secuencial|1", out var sGuardado))
{
    (tSec, dSec) = sGuardado;
    Console.WriteLine($"Secuencial: se reutiliza {tSec:F1} s (guardado).");
}
else
{
    var tiemposSec = new List<double>();
    for (int i = 0; i < REPETICIONES; i++)
    {
        var f = fabrica();
        sw.Restart();
        await Secuencial.Ejecutar(juegos, f);
        sw.Stop();
        tiemposSec.Add(sw.Elapsed.TotalSeconds);
        Console.WriteLine($"  secuencial, repetición {i + 1}/{REPETICIONES}: {sw.Elapsed.TotalSeconds:F1} s ({DateTime.Now:HH:mm})");
    }
    tSec = tiemposSec.Average();
    dSec = Desviacion(tiemposSec);
}

var modos = new (string nombre, bool n2, bool n3)[]
{
    ("Solo nivel 1", false, false),
    ("Niveles 1+2", true, false),
    ("Niveles 1+2+3", true, true),
};

var csv = new StringBuilder();
// Columnas nuevas al FINAL (no se quita ninguna): desv_s = desviación estándar de las repeticiones; reps; juegos.
csv.AppendLine("modo,trabajadores,tiempo_s,speedup,eficiencia_pct,desv_s,reps,juegos");
csv.AppendLine(string.Format(CultureInfo.InvariantCulture, "Secuencial,1,{0:F2},1.000,100.0,{1:F2},{2},{3}", tSec, dSec, REPETICIONES, juegos.Count));
File.WriteAllText(archivoResultados, csv.ToString());

Console.WriteLine($"\nNúcleos lógicos: {maximo} | Juegos: {juegos.Count} | Repeticiones: {REPETICIONES}");
Console.WriteLine($"Tiempo secuencial (promedio): {tSec:F1} s\n");
Console.WriteLine($"{"Modo",-15}{"Trab.",6}{"Tiempo(s)",12}{"Desv.",8}{"Speedup",10}{"Efic.",9}");
Console.WriteLine(new string('-', 60));

foreach (var (nombre, n2, n3) in modos)
{
    // En real, el análisis por niveles se mide solo con el máximo; la matriz completa, solo con los 3 niveles.
    foreach (int n in (!real || (n2 && n3)) ? configuraciones : new List<int> { maximo })
    {
        double t, desv;
        bool guardado = previas.TryGetValue($"{nombre}|{n}", out var g);
        if (guardado) (t, desv) = g;
        else
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
            t = ts.Average();
            desv = Desviacion(ts);
        }
        double speedup = tSec / t;
        double efic = speedup / n * 100;   // eficiencia = speedup / n.º de trabajadores DEL NIVEL 1

        Console.WriteLine($"{nombre,-15}{n,6}{t,12:F1}{desv,8:F1}{speedup,10:F2}{efic.ToString("F1", CultureInfo.InvariantCulture) + "%",9}" +
                          (guardado ? "  (guardado)" : $"  ({DateTime.Now:HH:mm})") + (efic > 100 ? "  *" : ""));
        csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "{0},{1},{2:F2},{3:F3},{4:F1},{5:F2},{6},{7}", nombre, n, t, speedup, efic, desv, REPETICIONES, juegos.Count));
        File.WriteAllText(archivoResultados, csv.ToString());   // guarda tras cada fila: si se cae, no se pierde lo anterior
    }
    Console.WriteLine();
}

Console.WriteLine("Nota: eficiencia = speedup / n.º de trabajadores del nivel 1 (los juegos que se procesan a la vez).");
Console.WriteLine("* Eficiencia mayor a 100 %: con los niveles 2 y 3 activos, cada trabajador del nivel 1 lanza además");
Console.WriteLine("  varias consultas a la vez (fuentes y tiendas), así que hay MÁS solicitudes en vuelo que trabajadores.");
Console.WriteLine("  No es un error: la cuenta usa solo los trabajadores del nivel 1 (ver sección 5 del documento técnico).");
Console.WriteLine($"Guardado en {archivoResultados}");
