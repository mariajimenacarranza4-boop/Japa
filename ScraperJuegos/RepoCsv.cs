using System.Globalization;
using System.Text;

namespace ScraperJuegos;

/// <summary>
/// Lee el REPO desde un CSV separado por punto y coma (;).
/// - Se lee en CADA ejecución: agregar/quitar una fila cambia el resultado sin recompilar.
/// - Las columnas se identifican por el NOMBRE del encabezado (no por posición), así que
///   se pueden reordenar o agregar columnas extra sin romper nada.
/// - Obligatorias: id, nombre, plataforma, fecha_lanzamiento, precio_sugerido.
///   Opcionales: imagen_url, steam_appid, nombre_busqueda, metacritic_slug, hltb_id.
/// - Soporta campos entre comillas ("Nombre; con punto y coma").
/// - Una fila mala se reporta como error pero NO detiene la carga.
/// </summary>
public class RepoCsv : IRepoJuegos
{
    private readonly string _ruta;
    private static readonly string[] FormatosFecha = { "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy" };
    private static readonly string[] Obligatorias =
        { "id", "nombre", "plataforma", "fecha_lanzamiento", "precio_sugerido" };

    public RepoCsv(string ruta) => _ruta = ruta;

    public ResultadoCarga Cargar()
    {
        var juegos = new List<Juego>();
        var errores = new List<string>();

        if (!File.Exists(_ruta))
        {
            errores.Add($"No se encontró el archivo del REPO: {_ruta}");
            return new ResultadoCarga(juegos, errores);
        }
        string contenido;
        try
        {
            // FileShare.ReadWrite: se puede leer aunque el CSV esté abierto en Excel (con ReadAllText daría IOException).
            using var fs = new FileStream(_ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            contenido = sr.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errores.Add($"No se pudo leer el archivo del REPO ({_ruta}): {ex.Message}");
            return new ResultadoCarga(juegos, errores);
        }
        return Parsear(contenido, juegos, errores);
    }

    /// <summary>Parsea el contenido del CSV (público para poder probarlo sin archivos).</summary>
    public static ResultadoCarga ParsearTexto(string contenido) =>
        Parsear(contenido, new List<Juego>(), new List<string>());

    private static ResultadoCarga Parsear(string contenido, List<Juego> juegos, List<string> errores)
    {
        var lineas = contenido.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        if (lineas.Length == 0 || string.IsNullOrWhiteSpace(lineas[0]))
        {
            errores.Add("El REPO está vacío o no tiene encabezado.");
            return new ResultadoCarga(juegos, errores);
        }

        var encabezado = DividirLinea(lineas[0].TrimStart('\uFEFF'))
            .Select(h => h.Trim().ToLowerInvariant()).ToList();
        var faltan = Obligatorias.Where(o => !encabezado.Contains(o)).ToList();
        if (faltan.Count > 0)
        {
            errores.Add($"Encabezado inválido: faltan las columnas {string.Join(", ", faltan)}.");
            return new ResultadoCarga(juegos, errores);
        }
        int Col(string n) => encabezado.IndexOf(n);

        var idsVistos = new HashSet<int>();
        var clavesVistas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 1; i < lineas.Length; i++)
        {
            int numeroLinea = i + 1;
            if (string.IsNullOrWhiteSpace(lineas[i])) continue;

            var c = DividirLinea(lineas[i]);
            string Campo(string n)
            {
                int k = Col(n);
                return k >= 0 && k < c.Count ? c[k].Trim() : "";
            }

            if (c.Count < Obligatorias.Length)
            {
                errores.Add($"Línea {numeroLinea}: tiene {c.Count} columnas y se esperaban al menos {Obligatorias.Length}.");
                continue;
            }
            if (!int.TryParse(Campo("id"), out int id))
            {
                errores.Add($"Línea {numeroLinea}: el id '{Campo("id")}' no es un número.");
                continue;
            }
            string nombre = Campo("nombre");
            string plataforma = Catalogos.NormalizarPlataforma(Campo("plataforma"));
            if (nombre.Length == 0 || plataforma.Length == 0)
            {
                errores.Add($"Línea {numeroLinea}: nombre o plataforma vacíos.");
                continue;
            }
            if (!Catalogos.Plataformas.Contains(plataforma))
            {
                errores.Add($"Línea {numeroLinea}: plataforma '{Campo("plataforma")}' no válida " +
                            $"(use: {string.Join(", ", Catalogos.Plataformas)}).");
                continue;
            }
            if (!DateTime.TryParseExact(Campo("fecha_lanzamiento"), FormatosFecha, CultureInfo.InvariantCulture,
                                        DateTimeStyles.None, out DateTime fecha))
            {
                errores.Add($"Línea {numeroLinea}: fecha inválida '{Campo("fecha_lanzamiento")}' (use aaaa-mm-dd).");
                continue;
            }
            string textoPrecio = Campo("precio_sugerido").Replace(',', '.');
            if (!decimal.TryParse(textoPrecio, NumberStyles.Number, CultureInfo.InvariantCulture,
                                  out decimal precio) || precio <= 0)
            {
                errores.Add($"Línea {numeroLinea}: precio inválido '{Campo("precio_sugerido")}'.");
                continue;
            }
            if (!idsVistos.Add(id))
            {
                errores.Add($"Línea {numeroLinea}: el id {id} está repetido.");
                continue;
            }
            if (!clavesVistas.Add($"{nombre}|{plataforma}"))
            {
                errores.Add($"Línea {numeroLinea}: '{nombre}' en {plataforma} está repetido.");
                continue;
            }

            string? Opc(string n) { var v = Campo(n); return v.Length == 0 ? null : v; }
            juegos.Add(new Juego(id, nombre, plataforma, fecha, precio,
                Opc("imagen_url"), Opc("steam_appid"), Opc("nombre_busqueda"),
                Opc("metacritic_slug"), Opc("hltb_id")));
        }
        PropagarHltbId(juegos);
        return new ResultadoCarga(juegos, errores);
    }

    /// <summary>Divide una línea por ';' respetando comillas dobles ("" = comilla literal).</summary>
    internal static List<string> DividirLinea(string linea)
    {
        var campos = new List<string>();
        var sb = new StringBuilder();
        bool entreComillas = false;
        for (int i = 0; i < linea.Length; i++)
        {
            char ch = linea[i];
            if (entreComillas)
            {
                if (ch == '"')
                {
                    if (i + 1 < linea.Length && linea[i + 1] == '"') { sb.Append('"'); i++; }
                    else entreComillas = false;
                }
                else sb.Append(ch);
            }
            else if (ch == '"') entreComillas = true;
            else if (ch == ';') { campos.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(ch);
        }
        campos.Add(sb.ToString());
        return campos;
    }

    /// <summary>
    /// El hltb_id es del JUEGO, no de la plataforma: si una fila de "Elden Ring" lo trae, las demás filas
    /// del mismo juego (PS5, PC, Xbox...) lo heredan. Así se escribe una sola vez por juego.
    /// </summary>
    private static void PropagarHltbId(List<Juego> juegos)
    {
        var porNombre = juegos.Where(j => !string.IsNullOrWhiteSpace(j.HltbId))
                              .GroupBy(j => j.Nombre, StringComparer.OrdinalIgnoreCase)
                              .ToDictionary(g => g.Key, g => g.First().HltbId, StringComparer.OrdinalIgnoreCase);
        if (porNombre.Count == 0) return;
        for (int i = 0; i < juegos.Count; i++)
            if (string.IsNullOrWhiteSpace(juegos[i].HltbId) && porNombre.TryGetValue(juegos[i].Nombre, out var id))
                juegos[i] = juegos[i] with { HltbId = id };
    }
}
