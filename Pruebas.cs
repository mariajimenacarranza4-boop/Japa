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
                await Task.Delay(400);   // mayor que la pausa (100 ms) para que SÍ se llegue al tope de 3
                Interlocked.Decrement(ref actual);
            }
        }));
        long ms = reloj.ElapsedMilliseconds;
        Verificar("Límite por sitio: llega a 3 y nunca pasa de 3", maximo == 3, $"(máximo observado: {maximo})");
        Verificar("Pausa entre solicitudes respetada", ms >= 2750, $"(30 solicitudes tardaron {ms} ms; mínimo esperado ~2900)");

        // 1b) Límite propio de un sitio sensible: ese dominio va de 1 en 1 y con más pausa; los demás conservan el general.
        {
            var limE = new LimitadorA(maxPorSitio: 3, pausaMs: 20,
                porSitio: new Dictionary<string, (int Max, int PausaMs)> { ["lento.test"] = (1, 150) });
            int dentroL = 0, maxL = 0, dentroR = 0, maxR = 0;
            var relojE = Stopwatch.StartNew();
            async Task Usar(string dom, Func<int> entra, Action sale)
            {
                using (await limE.EntrarAsync(dom)) { entra(); await Task.Delay(40); sale(); }
            }
            await Task.WhenAll(
                Enumerable.Range(0, 8).Select(_ => Usar("lento.test",
                    () => { int n = Interlocked.Increment(ref dentroL); int m; while (n > (m = Volatile.Read(ref maxL)) && Interlocked.CompareExchange(ref maxL, n, m) != m) { } return n; },
                    () => Interlocked.Decrement(ref dentroL)))
                .Concat(Enumerable.Range(0, 8).Select(_ => Usar("normal.test",
                    () => { int n = Interlocked.Increment(ref dentroR); int m; while (n > (m = Volatile.Read(ref maxR)) && Interlocked.CompareExchange(ref maxR, n, m) != m) { } return n; },
                    () => Interlocked.Decrement(ref dentroR)))));
            Verificar("Límite por sitio sensible: 1 simultánea y pausa de 150 ms (8 solicitudes >= ~1000 ms); el resto sigue con el límite general",
                maxL == 1 && maxR >= 2 && relojE.ElapsedMilliseconds >= 1000, $"(lento máx {maxL}, normal máx {maxR}, {relojE.ElapsedMilliseconds} ms)");
        }

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

        // 4) Catálogo: un juego quitado del REPO sale en una corrida completa y se conserva en una parcial.
        string cat = Path.GetTempFileName();
        RegistroJuego Reg(string id) => new() { Id = id, Nombre = "J" + id };
        Catalogo.Actualizar(cat, new List<RegistroJuego> { Reg("1"), Reg("2") });
        Catalogo.Actualizar(cat, new List<RegistroJuego> { Reg("1") });                                   // parcial
        int tras = System.Text.Json.JsonSerializer.Deserialize<List<RegistroJuego>>(File.ReadAllText(cat), Serializacion.Opciones)!.Count;
        Catalogo.Actualizar(cat, new List<RegistroJuego> { Reg("1") }, new HashSet<string> { "1" });     // completa
        int tras2 = System.Text.Json.JsonSerializer.Deserialize<List<RegistroJuego>>(File.ReadAllText(cat), Serializacion.Opciones)!.Count;
        Verificar("Catálogo: parcial conserva (2), completa elimina el quitado (1)", tras == 2 && tras2 == 1, $"({tras} y {tras2})");
        File.Delete(cat);

        // 5) Si todas las fuentes fallan, ultima_actualizacion no avanza.
        var viejo = Reg("7"); viejo.UltimaActualizacion = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var nuevo = Reg("7"); nuevo.EstadoFuentes["Steam"] = "fallo"; nuevo.EstadoFuentes["hltb"] = "fallo"; nuevo.UltimaActualizacion = DateTimeOffset.UtcNow;
        var (fus, _) = Catalogo.Fusionar(nuevo, viejo);
        Verificar("Todas las fuentes fallan: la fecha global conserva la anterior", fus.UltimaActualizacion == viejo.UltimaActualizacion);

        // 6) Un precio viejo y sospechoso de Eneba NO se repone al fusionar, y "limpiar" lo quita del catálogo.
        {
            var sospechoso = ResultadoTienda.Exito("Eneba", 0.26m, 0.26m, "digital", "PC", "https://x/doom", null);
            var sano = ResultadoTienda.Exito("Steam", 19.99m, 19.99m, "digital", "PC", "https://x/steam", null);
            var anterior = new RegistroJuego { Id = "9", Nombre = "Doom", PrecioSugerido = 19.99m, Tiendas = { sospechoso, sano } };
            var actual2 = new RegistroJuego { Id = "9", Nombre = "Doom", PrecioSugerido = 19.99m, Tiendas = { sano } };
            var (fusion, _) = Catalogo.Fusionar(actual2, anterior);
            Verificar("Fusionar: no repone un precio de Eneba sospechoso ($0.26 vs sugerido 19.99)",
                fusion.Tiendas.All(t => t.Tienda != "Eneba") && fusion.MejorTienda == "Steam" && fusion.DescuentoPct == 0m);

            string sucio = Path.GetTempFileName();
            var malo = new RegistroJuego { Id = "9", Nombre = "Doom", PrecioSugerido = 19.99m, Tiendas = { sospechoso, sano },
                MejorPrecio = 0.26m, MejorTienda = "Eneba", DescuentoPct = 98.7m, EnOferta = true };
            malo.EstadoFuentes["Eneba"] = "ok"; malo.EstadoFuentes["Epic Games Store"] = "fallo";
            Catalogo.Actualizar(sucio, new List<RegistroJuego> { malo });
            int quitados = Catalogo.Limpiar(sucio);
            var limpio = System.Text.Json.JsonSerializer.Deserialize<List<RegistroJuego>>(File.ReadAllText(sucio), Serializacion.Opciones)![0];
            Verificar("Limpiar: quita el precio sospechoso, recalcula mejor precio/oferta y ajusta estado_fuentes",
                quitados == 1 && limpio.MejorTienda == "Steam" && limpio.MejorPrecio == 19.99m && !limpio.EnOferta
                && limpio.EstadoFuentes["Eneba"] == "fallo" && !limpio.EstadoFuentes.ContainsKey("Epic Games Store"));
            File.Delete(sucio);
        }

        Console.WriteLine(fallos == 0 ? "\nTodas las pruebas pasaron." : $"\n{fallos} prueba(s) fallaron.");
        return fallos;
    }
}
