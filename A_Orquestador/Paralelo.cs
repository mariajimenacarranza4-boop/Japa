using ScraperJuegos;

public static class Paralelo
{
    // --- Nivel 3: una tienda para una fila (juego+plataforma) ---
    private static async Task<ResultadoTienda> ConsultarTienda(ITiendaScraper t, Juego j)
    {
        string fuente = $"{t.Tienda}/{j.Plataforma}";
        Log.Info(j.Nombre, "NIVEL 3", $"{fuente} consultando");
        var r = await TiendasDisponibles.ConsultarSeguroAsync(t, j);   // nunca lanza
        if (r.Ok) Log.Info(j.Nombre, "NIVEL 3", $"{fuente} OK precio {r.PrecioActual}");
        else Log.Fallo(j.Nombre, fuente, r.Error);
        return r;
    }

    private static async Task<List<ResultadoTienda>> Tiendas(JuegoAgrupado g, Fuentes f, bool paralelo)
    {
        // Solo las tiendas que venden esa plataforma (ej. Steam solo PC)
        var pares = g.Filas
            .SelectMany(j => f.Tiendas.Where(t => t.SoportaPlataforma(j.Plataforma)).Select(t => (t, j)))
            .ToList();

        if (paralelo)
            return (await Task.WhenAll(pares.Select(p => ConsultarTienda(p.t, p.j)))).ToList();

        var lista = new List<ResultadoTienda>();
        foreach (var (t, j) in pares) lista.Add(await ConsultarTienda(t, j));
        return lista;
    }

    // --- Nivel 2: fuentes del juego ---
    private static async Task<List<ResultadoMetacritic>> Metacritic(JuegoAgrupado g, Fuentes f)
    {
        var lista = new List<ResultadoMetacritic>();
        foreach (var j in g.Filas)   // Metacritic es POR consola del REPO
        {
            string fuente = $"metacritic/{j.Plataforma}";
            Log.Info(j.Nombre, "NIVEL 2", $"{fuente} consultando");
            var r = await Fuentes.Seguro(() => f.Metacritic.ConsultarAsync(j), ResultadoMetacritic.Fallo);
            if (r.Ok) Log.Info(j.Nombre, "NIVEL 2", $"{fuente} OK score {r.Score}");
            else Log.Fallo(j.Nombre, fuente, r.Error);
            lista.Add(r);
        }
        return lista;
    }

    private static async Task<ResultadoHltb> Hltb(JuegoAgrupado g, Fuentes f)
    {
        // HLTB es por juego, no por consola: se usa la fila que tenga hltb_id
        var j = g.Filas.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.HltbId)) ?? g.Filas[0];
        Log.Info(j.Nombre, "NIVEL 2", "hltb consultando");
        var r = await Fuentes.Seguro(() => f.Hltb.ConsultarAsync(j), ResultadoHltb.Fallo);
        if (r.Ok) Log.Info(j.Nombre, "NIVEL 2", $"hltb OK horas {r.HorasCompleto}");
        else Log.Fallo(j.Nombre, "hltb", r.Error);
        return r;
    }

    // Procesa UN juego. nivel2/nivel3 = false -> esa parte se hace en serie (sirve para el secuencial y para medir cada nivel).
    public static async Task<RegistroJuego> ProcesarJuego(JuegoAgrupado g, Fuentes f, bool nivel2, bool nivel3)
    {
        Log.Info(g.Nombre, "NIVEL 1", "inicia");
        List<ResultadoTienda> tiendas;
        List<ResultadoMetacritic> mcs;
        ResultadoHltb hl;

        if (nivel2)
        {
            Log.Info(g.Nombre, "NIVEL 2", "lanza: tiendas, metacritic, hltb a la vez");
            var tT = Tiendas(g, f, nivel3);
            var tM = Metacritic(g, f);
            var tH = Hltb(g, f);
            await Task.WhenAll(tT, tM, tH);
            tiendas = await tT; mcs = await tM; hl = await tH;
        }
        else
        {
            tiendas = await Tiendas(g, f, nivel3);
            mcs = await Metacritic(g, f);
            hl = await Hltb(g, f);
        }

        var registro = Consolidador.Armar(g, tiendas, mcs, hl);
        Log.Info(g.Nombre, "NIVEL 1", "TERMINADO");
        return registro;
    }

    // Nivel 1: juegos en paralelo. Cada tarea escribe SOLO su propia posición del arreglo
    // (índices distintos) -> no hay condición de carrera y el orden queda igual al del REPO.
    public static async Task<List<RegistroJuego>> Ejecutar(
        List<JuegoAgrupado> juegos, Fuentes f, int trabajadores, bool nivel2, bool nivel3)
    {
        var resultados = new RegistroJuego[juegos.Count];
        var opciones = new ParallelOptions { MaxDegreeOfParallelism = trabajadores };

        await Parallel.ForEachAsync(Enumerable.Range(0, juegos.Count), opciones, async (i, ct) =>
        {
            resultados[i] = await ProcesarJuego(juegos[i], f, nivel2, nivel3);
        });

        return resultados.ToList();
    }
}
