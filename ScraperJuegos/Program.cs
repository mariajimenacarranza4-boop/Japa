using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using ScraperJuegos;
using ScraperJuegos.Pruebas;

Console.OutputEncoding = System.Text.Encoding.UTF8;

// ---------------------------------------------------------------------------------
//  ScraperJuegos - parte de B (REPO + scrapers).  Uso:
//    dotnet run                              resumen y validación del REPO (200+ juegos)
//    dotnet run -- probar [N]                corre las 4 tiendas (Epic vía CheapShark) + Metacritic + HLTB sobre N juegos del REPO (por defecto 5)
//    dotnet run -- probar --juego "X"        un juego; si NO está en el REPO se consulta igual (opcional: --plataforma PS5 --precio 59.99 --anio 2023)
//    dotnet run -- hltb-pendientes           lista los juegos del REPO que aún no tienen hltb_id (salida/hltb_pendientes.csv)
//    dotnet run -- hltb-importar [--archivo X.csv]  copia los ids que llenó en hltb_pendientes.csv hacia Data/juegos.csv
//    dotnet run -- hltb-autocompletar [--aplicar]   descubre solo los hltb_id faltantes (vía Wikidata) y los escribe en salida/hltb_autocompletar.csv; con --aplicar también los copia a Data/juegos.csv
//    dotnet run -- hltb-verificar            lee la página /game/ID de cada hltb_id del REPO y confirma que es ese juego y trae tiempos
//    dotnet run -- diagnostico [--hltb-id N] robots.txt de cada sitio, respuestas crudas y prueba rápida (salida/diagnostico/)
//    dotnet run -- capturar "URL" nombre.html   guarda una página cruda en salida/diagnostico/ (respeta robots.txt)
//    dotnet run -- fechas-verificar [N] [--aplicar]  compara fecha_lanzamiento del REPO con la de Metacritic (por plataforma); reporte en salida/fechas_verificar.csv; --aplicar corrige el CSV
//    dotnet run -- autotest                  pruebas sin internet
//    dotnet run -- catalogos                 exporta valores válidos para los filtros de C
//    dotnet run -- exportar                  exporta el REPO a JSON (para cargarlo a la base en la nube)
//  Opciones: --hltb-id N (con 'probar': usa ese id de HowLongToBeat)   --repo RUTA.csv   --juego "Nombre"   --plataforma PS5   --salida carpeta
// ---------------------------------------------------------------------------------

string comando = args.Length > 0 && !args[0].StartsWith("--") ? args[0].ToLowerInvariant() : "repo";
string? Opcion(string nombre) { int i = Array.IndexOf(args, nombre); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }

string rutaCsv = Opcion("--repo") ?? Path.Combine(AppContext.BaseDirectory, "Data", "juegos.csv");
string carpetaSalida = Opcion("--salida") ?? Path.Combine(AppContext.BaseDirectory, "salida");

try
{
    switch (comando)
    {
        case "autotest": return AutoTests.Ejecutar(rutaCsv);
        case "probar": return await Probar();
        case "diagnostico": return await Diagnostico.EjecutarAsync(carpetaSalida, Opcion("--hltb-id"), rutaCsv);
        case "capturar": return await Diagnostico.CapturarAsync(carpetaSalida, args.Length > 1 ? args[1] : null, args.Length > 2 ? args[2] : null);
        case "hltb-pendientes": return HltbPendientes();
        case "hltb-importar": return HltbImportar();
        case "hltb-autocompletar": return await HltbAutocompletar();
        case "hltb-verificar": return await HltbVerificar();
        case "fechas-verificar": return await FechasVerificar();
        case "catalogos": return Catalogo();
        case "exportar": return Exportar();
        default: return ResumenRepo();
    }
}
catch (Exception ex)
{
    // Nada debe cerrar el programa con un volcado de excepción: se explica y se sale con código 1.
    Console.WriteLine();
    Console.WriteLine($"Error inesperado ({ex.GetType().Name}): {ex.Message}");
    Console.WriteLine("Revise el archivo del REPO, la carpeta de salida y la conexión, y vuelva a intentar.");
    return 1;
}

int ResumenRepo()
{
    IRepoJuegos repo = new RepoCsv(rutaCsv);
    ResultadoCarga r = repo.Cargar();
    var juegos = r.AgruparPorJuego();

    Console.WriteLine($"Archivo: {rutaCsv}");
    Console.WriteLine($"Juegos distintos (lo que pide el enunciado): {juegos.Count}");
    Console.WriteLine($"Filas válidas (juego + plataforma):          {r.Juegos.Count}");
    Console.WriteLine();
    Console.WriteLine("Filas por plataforma  (cobertura de tiendas):");
    // Se arma desde TiendasDisponibles (única lista de tiendas): al agregar una tienda aparece aquí sola.
    var tiendas = TiendasDisponibles.Crear(ClienteHttp.Crear())
        .Select(t => new ITiendaScraperInfo(t.Tienda, Catalogos.Plataformas.Where(x => t.SoportaPlataforma(x)).ToArray()))
        .ToArray();
    foreach (var g in r.Juegos.GroupBy(j => j.Plataforma).OrderByDescending(g => g.Count()))
    {
        var quien = tiendas.Where(t => t.Plataformas.Contains(g.Key)).Select(t => t.Nombre).ToList();
        Console.WriteLine($"  {g.Key,-12} {g.Count(),4}   tienda: {(quien.Count > 0 ? string.Join(", ", quien) : "— (sin tienda implementada)")}");
    }
    int sinTienda = juegos.Count(j => !j.Filas.Any(f => tiendas.Any(t => t.Plataformas.Contains(f.Plataforma))));
    int dosTiendas = juegos.Count(j => j.Filas.Select(f => tiendas.FirstOrDefault(t => t.Plataformas.Contains(f.Plataforma))?.Nombre)
                                             .Where(x => x != null).Distinct().Count() >= 2);
    Console.WriteLine();
    Console.WriteLine($"Juegos con precio en 2 tiendas distintas: {dosTiendas}/{juegos.Count}");
    Console.WriteLine($"Juegos sin ninguna tienda: {sinTienda}/{juegos.Count}");

    int conExtras = r.Juegos.Count(j => j.SteamAppId != null || j.NombreBusqueda != null || j.MetacriticSlug != null || j.HltbId != null || j.ImagenUrl != null);
    Console.WriteLine($"Filas con datos extra (appid/slug/hltb/imagen/nombre_busqueda): {conExtras}");

    if (r.Errores.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine($"Errores encontrados ({r.Errores.Count}):");
        foreach (string e in r.Errores) Console.WriteLine($"  - {e}");
    }
    Console.WriteLine();
    bool ok = juegos.Count >= 200 && r.Errores.Count == 0;
    Console.WriteLine(juegos.Count >= 200
        ? $"OK: el REPO tiene {juegos.Count} juegos distintos (mínimo 200)."
        : $"ATENCIÓN: el REPO tiene solo {juegos.Count} juegos distintos; el enunciado pide 200 o más (las filas por plataforma no cuentan doble).");
    return ok ? 0 : 1;
}

async Task<int> Probar()
{
    int n = args.Length > 1 && int.TryParse(args[1], out int k) && k > 0 ? k : 5;
    var repoRes = new RepoCsv(rutaCsv).Cargar();
    foreach (string e in repoRes.Errores.Take(5)) Console.WriteLine("  (aviso del REPO) " + e);

    string? nombreBuscado = Opcion("--juego");
    string? platTexto = Opcion("--plataforma");
    string? plat = platTexto != null ? Catalogos.NormalizarPlataforma(platTexto) : null;
    if (plat != null && !Catalogos.Plataformas.Contains(plat))
    {
        Console.WriteLine($"Plataforma '{platTexto}' no válida. Use: {string.Join(", ", Catalogos.Plataformas)}");
        return 1;
    }

    IEnumerable<Juego> lista = repoRes.Juegos;
    if (nombreBuscado != null) lista = lista.Where(j => j.Nombre.Contains(nombreBuscado, StringComparison.OrdinalIgnoreCase));
    if (plat != null) lista = lista.Where(j => j.Plataforma.Equals(plat, StringComparison.OrdinalIgnoreCase));
    // Por defecto, una muestra variada: juegos populares y de varias plataformas
    List<Juego> muestra = (nombreBuscado != null || plat != null) ? lista.Take(n).ToList()
        : repoRes.Juegos.Where(j => j.Id <= 30).GroupBy(j => j.Nombre).Select(g => g.First()).Take(n).ToList();

    // Juego que NO está en el REPO: no es un error, se consulta igual con los datos que se den (sin tocar el CSV).
    if (muestra.Count == 0 && nombreBuscado != null)
    {
        string nombreAd = nombreBuscado.Trim();
        decimal precioSug = decimal.TryParse((Opcion("--precio") ?? "0").Replace(',', '.'), NumberStyles.Number,
                                             CultureInfo.InvariantCulture, out var ps) && ps > 0 ? ps : 0m;
        DateTime fecha = int.TryParse(Opcion("--anio"), out int anio) && anio is >= 1970 and <= 2100
            ? new DateTime(anio, 1, 1) : DateTime.MinValue;
        string[] plats = plat != null ? new[] { plat } : new[] { "PC", "PS5", "Switch" };
        muestra = plats.Select((p, i) => new Juego(-(i + 1), nombreAd, p, fecha, precioSug)).ToList();
        Console.WriteLine($"'{nombreAd}' no está en el REPO: se consulta igual (fuera del REPO) en {string.Join(", ", plats)}.");
        Console.WriteLine("  Opciones: --plataforma PS5 | --precio 59.99 (para evaluar la oferta) | --anio 2023 (ayuda a HowLongToBeat).");
        Console.WriteLine();
    }
    if (muestra.Count == 0)
    {
        Console.WriteLine("No hay filas para probar (el REPO está vacío o ninguna coincide con los filtros).");
        return 1;
    }

    string? hltbIdOpcion = Opcion("--hltb-id");
    if (!string.IsNullOrWhiteSpace(hltbIdOpcion)) muestra = muestra.Select(j => j with { HltbId = hltbIdOpcion.Trim() }).ToList();

    Directory.CreateDirectory(carpetaSalida);
    string rutaLog = Path.Combine(carpetaSalida, "fallos.log");
    using var log = new LogFallosArchivo(rutaLog, escribirEnConsola: true);
    var limitador = new LimitadorDominio(maxConcurrenciaPorDominio: 2, pausaMinima: TimeSpan.FromMilliseconds(1000));
    var http = ClienteHttp.Crear(new ConfigScraping(), limitador, log);

    ITiendaScraper[] tiendas = TiendasDisponibles.Crear(http);
    var meta = new MetacriticScraper(http);
    var hltb = new HltbScraper(http);

    // Ni un scraper con un error de programación debe detener la prueba: cada consulta queda protegida.
    async Task<T> Proteger<T>(Func<Task<T>> consulta, Func<string, string?, T> fallo)
    {
        try { return await consulta(); }
        catch (Exception ex) { return fallo(TipoError.FormatoCambiado, $"{ex.GetType().Name}: {ex.Message}"); }
    }

    // Fecha y hora REALES de esta corrida (del reloj del equipo); cada resultado lleva además su propia fecha_consulta.
    Console.WriteLine($"Fecha y hora de la corrida: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} (hora de este equipo) / {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
    Console.WriteLine($"Probando {muestra.Count} filas contra los sitios REALES (pausa 1 s por dominio, robots.txt respetado).");
    Console.WriteLine($"Tiendas: {string.Join(", ", tiendas.Select(t => t.Tienda))}");
    if (TiendasDisponibles.NombresDesactivados().Length > 0)
        Console.WriteLine($"Tiendas desactivadas (TIENDAS_DESACTIVADAS): {string.Join(", ", TiendasDisponibles.NombresDesactivados())}");
    Console.WriteLine($"User-Agent: {http.Config.UserAgent}");
    if (http.Config.UserAgent.Contains("CONFIGURAR")) Console.WriteLine("  (defina la variable SCRAPER_CONTACTO con su correo para un User-Agent completo)");
    Console.WriteLine();

    var todos = new List<object>();
    var sw = Stopwatch.StartNew();
    foreach (var j in muestra)
    {
        string etiqueta = j.Id > 0 ? $"[{j.Id}]" : "[fuera del REPO]";
        string sugerido = j.PrecioSugerido > 0 ? $"sugerido ${j.PrecioSugerido}" : "sin precio sugerido";
        Console.WriteLine($"=== {etiqueta} {j.Nombre} ({j.Plataforma}) — {sugerido}");
        var resTiendas = new List<ResultadoTienda>();
        var aplicables = tiendas.Where(t => t.SoportaPlataforma(j.Plataforma)).ToList();
        if (aplicables.Count == 0) Console.WriteLine("  (ninguna tienda implementada para esta plataforma)");
        foreach (var t in aplicables)
        {
            var r = await TiendasDisponibles.ConsultarSeguroAsync(t, j);
            resTiendas.Add(r);
            Console.WriteLine($"  {t.Tienda}: " + (r.Ok
                ? $"${r.PrecioActual} (regular {(r.PrecioRegular.HasValue ? "$" + r.PrecioRegular : "N/D")}) {r.Formato} {r.Enlace}"
                : $"ERROR {r.Error} — {r.Detalle}"));
        }
        var rm = await Proteger(() => meta.ConsultarAsync(j), (tipo, det) => ResultadoMetacritic.Fallo(tipo, det));
        Console.WriteLine("  Metacritic: " + (rm.Ok ? $"{rm.Score} ({rm.Plataforma}, verificada={rm.Verificada})" : $"ERROR {rm.Error} — {rm.Detalle}"));
        var rh = await Proteger(() => hltb.ConsultarAsync(j), (tipo, det) => ResultadoHltb.Fallo(tipo, det));
        Console.WriteLine("  HLTB: " + (rh.Ok ? $"100% {rh.HorasCompleto?.ToString() ?? "N/D"} h, historia {rh.HorasPrincipal?.ToString() ?? "N/D"} h, historia+extras {rh.HorasExtra?.ToString() ?? "N/D"} h, todos {rh.HorasTodosLosEstilos?.ToString() ?? "N/D"} h; dev {rh.Desarrollador ?? "N/D"}; nota {rh.CalificacionUsuarios?.ToString() ?? "N/D"}; completaron {rh.Completaron?.ToString() ?? "N/D"} (fuente: HowLongToBeat; consultado {rh.FechaConsulta.ToLocalTime():yyyy-MM-dd HH:mm:ss})" : $"ERROR {rh.Error} — {rh.Detalle}"));
        var c = CalculadorOferta.Consolidar(resTiendas, j.PrecioSugerido);
        if (c.MejorPrecio != null) Console.WriteLine($"  -> mejor: ${c.MejorPrecio} en {c.MejorTienda}; en_oferta={c.EnOferta}; descuento={c.DescuentoPct} %");
        todos.Add(new { juego = j, tiendas = resTiendas, metacritic = rm, hltb = rh, consolidado = c });
    }
    string salida = Path.Combine(carpetaSalida, "prueba_scrapers.json");
    File.WriteAllText(salida, JsonSerializer.Serialize(todos, Serializacion.Opciones));
    Console.WriteLine();
    Console.WriteLine($"Listo en {sw.Elapsed.TotalSeconds:0.0} s. Resultados (formato del Contrato 1): {salida}");
    Console.WriteLine($"Log de fallos y reintentos: {rutaLog}");
    Console.WriteLine("Resumen de errores: " + (log.ContadoresPorTipo.IsEmpty ? "ninguno"
        : string.Join(", ", log.ContadoresPorTipo.Select(kv => $"{kv.Key}={kv.Value}"))));
    return 0;
}

int HltbImportar() => HltbImportarDesde(Opcion("--archivo") ?? Path.Combine(carpetaSalida, "hltb_pendientes.csv"));

int HltbImportarDesde(string origen)
{
    // Edita el CSV FUENTE (Data/juegos.csv del proyecto), no la copia de bin/: si no se da --repo, usa Data/juegos.csv de la carpeta actual.
    string destino = Opcion("--repo") ?? (File.Exists(Path.Combine("Data", "juegos.csv")) ? Path.Combine("Data", "juegos.csv") : rutaCsv);
    if (!File.Exists(origen)) { Console.WriteLine($"No existe {origen}. Corra primero: dotnet run -- hltb-pendientes"); return 1; }
    if (!File.Exists(destino)) { Console.WriteLine($"No existe el REPO {destino}."); return 1; }

    var ids = new Dictionary<int, string>(); // id de fila del REPO -> hltb_id
    int invalidos = 0;
    foreach (var linea in File.ReadAllLines(origen).Skip(1))
    {
        var c = RepoCsv.DividirLinea(linea);
        if (c.Count < 4) continue;
        string hid = c[2].Trim();
        if (hid.Length == 0) continue;
        // Acepta el número o la URL completa pegada (https://howlongtobeat.com/game/68151).
        var m = System.Text.RegularExpressions.Regex.Match(hid, @"^(?:https?://howlongtobeat\.com/game/)?(\d+)/?$");
        if (!m.Success || !int.TryParse(c[3].Trim(), out int fila)) { invalidos++; Console.WriteLine($"  Se ignora '{c[0]}': hltb_id '{hid}' no es un número ni una URL de HLTB."); continue; }
        ids[fila] = m.Groups[1].Value;
    }
    if (ids.Count == 0) { Console.WriteLine("No hay ids llenos en la columna hltb_id_a_llenar."); return invalidos > 0 ? 1 : 0; }

    string texto = File.ReadAllText(destino, System.Text.Encoding.UTF8);
    // ReadAllText ya quita el BOM, así que se detecta en los bytes: sin BOM, Excel muestra mal las tildes (Pokémon).
    var primeros = new byte[3]; using (var fsb = File.OpenRead(destino)) { _ = fsb.Read(primeros, 0, 3); }
    bool bom = primeros[0] == 0xEF && primeros[1] == 0xBB && primeros[2] == 0xBF;
    string fin = texto.Contains("\r\n") ? "\r\n" : "\n";
    var lineas = texto.TrimStart('\uFEFF').Replace("\r\n", "\n").Split('\n').ToList();
    var enc = RepoCsv.DividirLinea(lineas[0]).Select(h => h.Trim().ToLowerInvariant()).ToList();
    int cId = enc.IndexOf("id"), cH = enc.IndexOf("hltb_id");
    if (cId < 0) { Console.WriteLine("El REPO no tiene columna id."); return 1; }
    if (cH < 0) { lineas[0] += ";hltb_id"; cH = enc.Count; for (int i = 1; i < lineas.Count; i++) if (lineas[i].Length > 0) lineas[i] += ";"; enc.Add("hltb_id"); }

    static string Q(string v) => v.Contains(';') || v.Contains('"') ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    int hechos = 0;
    for (int i = 1; i < lineas.Count; i++)
    {
        if (string.IsNullOrWhiteSpace(lineas[i])) continue;
        var c = RepoCsv.DividirLinea(lineas[i]);
        if (c.Count <= cId || !int.TryParse(c[cId].Trim(), out int idFila) || !ids.TryGetValue(idFila, out var nuevo)) continue;
        while (c.Count <= cH) c.Add("");
        c[cH] = nuevo;
        lineas[i] = string.Join(";", c.Select(Q));
        hechos++;
    }
    File.WriteAllText(destino, (bom ? "\uFEFF" : "") + string.Join(fin, lineas), new System.Text.UTF8Encoding(false));
    Console.WriteLine($"Listo: {hechos} ids copiados a {destino} ({invalidos} ignorados). Ahora: dotnet run -- hltb-verificar");
    return 0;
}

async Task<int> HltbAutocompletar()
{
    var repo = new RepoCsv(rutaCsv).Cargar();
    var juegos = repo.AgruparPorJuego();
    var faltan = juegos.Where(g => g.Filas.All(f => string.IsNullOrWhiteSpace(f.HltbId))).ToList();
    if (faltan.Count == 0) { Console.WriteLine("Todos los juegos ya tienen hltb_id. Verifique con: dotnet run -- hltb-verificar"); return 0; }

    Directory.CreateDirectory(carpetaSalida);
    string rutaSalida = Path.Combine(carpetaSalida, "hltb_autocompletar.csv");
    using var log = new LogFallosArchivo(Path.Combine(carpetaSalida, "fallos.log"), escribirEnConsola: false);
    // Wikidata es una API pública pensada para uso programático (con User-Agent descriptivo): se usa 1 solicitud por segundo.
    // robots.txt de wikidata.org regula a los rastreadores de páginas, no a su API (etiqueta oficial de la API de Wikimedia),
    // por eso AQUÍ (y solo aquí) no se consulta; HLTB y las tiendas siguen respetando robots.txt siempre.
    var http = ClienteHttp.Crear(new ConfigScraping { RespetarRobots = false },
                                 new LimitadorDominio(1, TimeSpan.FromMilliseconds(1000)), log);
    if (http.Config.UserAgent.Contains("CONFIGURAR"))
        Console.WriteLine("AVISO: defina SCRAPER_CONTACTO con su correo (Wikimedia pide un User-Agent con contacto).");
    var resolver = new ResolverHltbWikidata(http);

    Console.WriteLine($"Fecha y hora: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}. Buscando hltb_id de {faltan.Count} juegos en Wikidata (1 solicitud/s; tarda unos minutos)...");
    static string Q(string v) => "\"" + v.Replace("\"", "\"\"") + "\"";
    var lineas = new List<string> { "nombre;anio;hltb_id_a_llenar;id_fila_donde_escribir_el_hltb_id;enlace_de_busqueda;estado_autocompletado" };
    int ok = 0, hecho = 0;
    try
    {
        foreach (var g in faltan)
        {
            var fila = g.Filas[0];
            var r = await resolver.ResolverAsync(fila);
            hecho++;
            if (r.Estado == "ok") ok++;
            string enlace = "https://howlongtobeat.com/?q=" + Uri.EscapeDataString(g.Nombre);
            lineas.Add($"{Q(g.Nombre)};{fila.FechaLanzamiento.Year};{r.HltbId};{fila.Id};{enlace};{Q(r.Estado + ": " + r.Detalle)}");
            Console.WriteLine($"  [{hecho}/{faltan.Count}] {(r.Estado == "ok" ? "OK    " : "FALTA ")} {g.Nombre,-40} {(r.Estado == "ok" ? "id " + r.HltbId : r.Estado)}");
        }
    }
    finally
    {
        // Aunque se interrumpa (Ctrl+C), lo avanzado queda guardado.
        File.WriteAllLines(rutaSalida, lineas, new System.Text.UTF8Encoding(true));
    }
    Console.WriteLine();
    Console.WriteLine($"Encontrados automáticamente: {ok}/{faltan.Count}. Archivo: {rutaSalida}");
    if (ok < faltan.Count)
        Console.WriteLine($"Los {faltan.Count - ok} restantes quedan en blanco en ese archivo (con enlace de búsqueda de HLTB): llénelos a mano si quiere cubrirlos.");
    if (args.Contains("--aplicar"))
    {
        if (ok == 0) { Console.WriteLine("No hay ids que aplicar."); return 0; }
        int cod = HltbImportarDesde(rutaSalida);
        Console.WriteLine("Siguiente paso: dotnet run -- hltb-verificar (confirma que cada id sea ese juego y traiga tiempos).");
        return cod;
    }
    Console.WriteLine($"Para copiarlos al REPO: dotnet run -- hltb-importar --archivo \"{rutaSalida}\"   (o repita este comando con --aplicar)");
    return 0;
}

int HltbPendientes()
{
    var repo = new RepoCsv(rutaCsv).Cargar();
    var juegos = repo.AgruparPorJuego();
    var faltan = juegos.Where(g => g.Filas.All(f => string.IsNullOrWhiteSpace(f.HltbId))).ToList();
    Directory.CreateDirectory(carpetaSalida);
    string ruta = Path.Combine(carpetaSalida, "hltb_pendientes.csv");
    var lineas = new List<string> { "nombre;anio;hltb_id_a_llenar;id_fila_donde_escribir_el_hltb_id" };
    foreach (var g in faltan)
        lineas.Add($"\"{g.Nombre.Replace("\"", "\"\"")}\";{g.Filas[0].FechaLanzamiento.Year};;{g.Filas[0].Id}");
    File.WriteAllLines(ruta, lineas, new System.Text.UTF8Encoding(true));
    Console.WriteLine($"Juegos con hltb_id: {juegos.Count - faltan.Count}/{juegos.Count}.  Sin hltb_id: {faltan.Count}.");
    Console.WriteLine($"Lista escrita en: {ruta}");
    Console.WriteLine("Cómo llenarlo: busque el juego en howlongtobeat.com (en su navegador), abra su página y copie el NÚMERO de la URL");
    Console.WriteLine("  (ej. https://howlongtobeat.com/game/68151 -> 68151) en la columna hltb_id de Data/juegos.csv, en UNA sola fila del juego;");
    Console.WriteLine("  las demás plataformas del mismo juego lo heredan. Luego corra: dotnet run -- hltb-verificar");
    return 0;
}

async Task<int> HltbVerificar()
{
    var repo = new RepoCsv(rutaCsv).Cargar();
    var porVerificar = repo.AgruparPorJuego().Select(g => g.Filas.FirstOrDefault(f => !string.IsNullOrWhiteSpace(f.HltbId))).Where(f => f != null).Select(f => f!).ToList();
    if (porVerificar.Count == 0) { Console.WriteLine("Ningún juego tiene hltb_id todavía. Use: dotnet run -- hltb-pendientes"); return 1; }
    Directory.CreateDirectory(carpetaSalida);
    using var log = new LogFallosArchivo(Path.Combine(carpetaSalida, "fallos.log"), escribirEnConsola: false);
    var http = ClienteHttp.Crear(new ConfigScraping(), new LimitadorDominio(1, TimeSpan.FromMilliseconds(1000)), log);
    var hltb = new HltbScraper(http);
    Console.WriteLine($"Fecha y hora: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}. Verificando {porVerificar.Count} hltb_id (solo /game/ID, pausa 1 s)...");
    int ok = 0;
    var erroneos = new List<(string nombre, string id)>();
    foreach (var j in porVerificar)
    {
        var r = await hltb.ConsultarAsync(j);
        if (r.Ok) ok++;
        else if (r.Error == TipoError.NoEncontrado && (r.Detalle ?? "").StartsWith("el hltb_id")) erroneos.Add((j.Nombre, j.HltbId!));
        Console.WriteLine($"  {(r.Ok ? "OK    " : "FALLA ")} {j.Nombre,-40} id {j.HltbId,-8} " +
            (r.Ok ? $"completo {r.HorasCompleto?.ToString() ?? "N/D"} h, principal {r.HorasPrincipal?.ToString() ?? "N/D"} h" : $"{r.Error}: {r.Detalle}"));
    }
    Console.WriteLine($"HLTB respondió bien: {ok}/{porVerificar.Count}");
    if (args.Contains("--limpiar") && erroneos.Count > 0)
    {
        // Ids que apuntan a OTRO juego: dejarlos daría tiempos equivocados. Se vacían (el sitio mostrará N/D) para llenarlos bien después.
        string destino = Opcion("--repo") ?? (File.Exists(Path.Combine("Data", "juegos.csv")) ? Path.Combine("Data", "juegos.csv") : rutaCsv);
        int quitados = EditorRepoCsv.QuitarHltbIds(destino, erroneos);
        Console.WriteLine($"--limpiar: se vació el hltb_id de {quitados} fila(s) con id equivocado en {destino}: {string.Join(", ", erroneos.Select(e => e.nombre))}.");
        Console.WriteLine("  Esos juegos quedan en N/D. Para corregirlos: busque el juego en howlongtobeat.com, copie el número de la URL a hltb_id y repita hltb-verificar.");
    }
    else if (erroneos.Count > 0)
        Console.WriteLine($"{erroneos.Count} id(s) apuntan a otro juego. Repita con --limpiar para vaciarlos del REPO (quedan en N/D).");
    return ok == porVerificar.Count ? 0 : 1;
}

async Task<int> FechasVerificar()
{
    var repo = new RepoCsv(rutaCsv).Cargar();
    int tope = args.Length > 1 && int.TryParse(args[1], out int k) && k > 0 ? k : int.MaxValue;
    var filas = repo.Juegos.Take(tope).ToList();
    Directory.CreateDirectory(carpetaSalida);
    using var log = new LogFallosArchivo(Path.Combine(carpetaSalida, "fallos.log"), escribirEnConsola: false);
    var http = ClienteHttp.Crear(new ConfigScraping(), new LimitadorDominio(1, TimeSpan.FromMilliseconds(1000)), log);
    var meta = new MetacriticScraper(http);
    Console.WriteLine($"Fecha y hora: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}. Comparando {filas.Count} filas con Metacritic (pausa 1 s)...");
    var reporte = new List<string> { "id;nombre;plataforma;fecha_repo;fecha_metacritic;estado" };
    var cambios = new Dictionary<int, string>();
    int iguales = 0, distintas = 0, sinDato = 0;
    foreach (var j in filas)
    {
        var r = await meta.ConsultarAsync(j);
        string repoF = j.FechaLanzamiento.ToString("yyyy-MM-dd");
        string estado;
        if (!r.Ok || r.FechaLanzamiento == null) { estado = r.Ok ? "sin_fecha" : r.Error ?? "falla"; sinDato++; }
        else if (r.FechaLanzamiento == repoF) { estado = "igual"; iguales++; }
        else
        {
            estado = "distinta"; distintas++;
            // Solo se corrige si Metacritic confirmó la plataforma; si no, queda para revisión manual.
            if (r.Verificada == true) cambios[j.Id] = r.FechaLanzamiento;
        }
        reporte.Add($"{j.Id};{j.Nombre.Replace(';', ',')};{j.Plataforma};{repoF};{r.FechaLanzamiento};{estado}");
        if (estado == "distinta") Console.WriteLine($"  DISTINTA {j.Nombre,-40} {j.Plataforma,-12} REPO {repoF}  Metacritic {r.FechaLanzamiento}");
    }
    string ruta = Path.Combine(carpetaSalida, "fechas_verificar.csv");
    File.WriteAllLines(ruta, reporte, new System.Text.UTF8Encoding(true));
    Console.WriteLine($"Iguales: {iguales}  Distintas: {distintas}  Sin dato de Metacritic: {sinDato}. Reporte: {ruta}");
    if (args.Contains("--aplicar") && cambios.Count > 0)
    {
        string destino = Opcion("--repo") ?? (File.Exists(Path.Combine("Data", "juegos.csv")) ? Path.Combine("Data", "juegos.csv") : rutaCsv);
        int n = EditorRepoCsv.ActualizarFechas(destino, cambios);
        Console.WriteLine($"--aplicar: se corrigió la fecha de {n} fila(s) en {destino} (solo donde Metacritic confirmó la plataforma).");
    }
    else if (cambios.Count > 0) Console.WriteLine($"{cambios.Count} fecha(s) se pueden corregir. Repita con --aplicar para escribirlas en el CSV.");
    return 0;
}

int Catalogo()
{
    Directory.CreateDirectory(carpetaSalida);
    string ruta = Path.Combine(carpetaSalida, "valores_validos.json");
    var repo = new RepoCsv(rutaCsv).Cargar();
    File.WriteAllText(ruta, JsonSerializer.Serialize(new
    {
        plataformas = Catalogos.Plataformas,
        formatos = Catalogos.Formatos,
        tiendas = Catalogos.Tiendas,
        tipos_de_juego = Catalogos.Formatos.Append("otro"),
        total_juegos = repo.AgruparPorJuego().Count
    }, Serializacion.Opciones));
    Console.WriteLine("Escrito: " + ruta);
    return 0;
}

int Exportar()
{
    Directory.CreateDirectory(carpetaSalida);
    string ruta = Path.Combine(carpetaSalida, "repo_juegos.json");
    var repo = new RepoCsv(rutaCsv).Cargar();
    if (repo.Errores.Count > 0) { Console.WriteLine("El REPO tiene errores; corríjalos primero:"); repo.Errores.ForEach(e => Console.WriteLine("  - " + e)); return 1; }
    var filas = repo.Juegos.Select(j => new
    {
        id = j.Id, nombre = j.Nombre, plataforma = j.Plataforma,
        fecha_lanzamiento = j.FechaLanzamiento.ToString("yyyy-MM-dd"), precio_sugerido = j.PrecioSugerido,
        imagen_url = j.ImagenUrl, steam_appid = j.SteamAppId, nombre_busqueda = j.NombreBusqueda,
        metacritic_slug = j.MetacriticSlug, hltb_id = j.HltbId
    });
    File.WriteAllText(ruta, JsonSerializer.Serialize(filas, Serializacion.Opciones));
    Console.WriteLine($"Escrito: {ruta} ({repo.Juegos.Count} filas, {repo.AgruparPorJuego().Count} juegos)");
    return 0;
}

record ITiendaScraperInfo(string Nombre, params string[] Plataformas);
