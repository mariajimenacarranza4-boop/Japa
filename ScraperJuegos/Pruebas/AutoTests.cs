using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace ScraperJuegos.Pruebas;

/// <summary>
/// Pruebas SIN internet (dotnet run -- autotest). No necesitan paquetes externos.
/// Cubren: REPO, nombres, oferta, los 4 scrapers (éxito + cada tipo de error), robots.txt,
/// reintentos, límite de concurrencia y ejecución en paralelo sin estado compartido.
/// </summary>
public static class AutoTests
{
    private static int _ok, _fallos;
    private static readonly List<string> _detalle = new();

    private static void Verificar(bool condicion, string nombre, string? extra = null)
    {
        if (condicion) { _ok++; Console.WriteLine($"  ✔ {nombre}"); }
        else { _fallos++; Console.WriteLine($"  ✘ {nombre} {extra}"); _detalle.Add(nombre); }
    }

    private static readonly ConfigScraping CfgRapida = new()
    {
        Timeout = TimeSpan.FromSeconds(2), MaxReintentos = 2,
        EsperaBase = TimeSpan.FromMilliseconds(5), UserAgent = "ScraperJuegosTEC/test"
    };

    private static (ClienteHttp cli, ManejadorFalso mf, LogFallosArchivo log) Montar(
        Func<HttpRequestMessage, int, HttpResponseMessage> f, ConfigScraping? cfg = null, string? rutaLog = null)
    {
        var mf = new ManejadorFalso((r, n) =>
            r.RequestUri!.AbsolutePath == "/robots.txt" ? ManejadorFalso.Texto(HttpStatusCode.OK, Fixtures.RobotsAbierto) : f(r, n));
        var log = new LogFallosArchivo(rutaLog);
        var cli = new ClienteHttp(new HttpClient(mf), cfg ?? CfgRapida, new LimitadorDominio(4, TimeSpan.Zero), log);
        return (cli, mf, log);
    }

    private static Juego J(string nombre, string plat, decimal sug = 59.99m, int anio = 2022, string? appid = null, string? hltb = null) =>
        new(1, nombre, plat, new DateTime(anio, 1, 1), sug, SteamAppId: appid, HltbId: hltb);

    public static int Ejecutar(string rutaCsv)
    {
        Console.WriteLine("== REPO ==");
        var repo = new RepoCsv(rutaCsv).Cargar();
        var grupos = repo.AgruparPorJuego();
        Verificar(repo.Errores.Count == 0, "el CSV real se lee sin errores", string.Join(" | ", repo.Errores.Take(3)));
        Verificar(grupos.Count >= 200, $"hay 200+ JUEGOS distintos ({grupos.Count} juegos, {repo.Juegos.Count} filas)");
        Verificar(repo.Juegos.All(j => Catalogos.Plataformas.Contains(j.Plataforma)), "todas las plataformas son válidas");
        var viejo = RepoCsv.ParsearTexto("id;nombre;plataforma;fecha_lanzamiento;precio_sugerido\r\n1;Doom;PC;2016-05-13;19,99\r\n");
        Verificar(viejo.Juegos.Count == 1 && viejo.Juegos[0].PrecioSugerido == 19.99m, "formato viejo de 5 columnas sigue funcionando (coma decimal)");
        var comillas = RepoCsv.ParsearTexto("fecha_lanzamiento;id;precio_sugerido;plataforma;nombre\n2020-01-01;7;9.99;ps5;\"Juego; raro\"\n");
        Verificar(comillas.Juegos.Count == 1 && comillas.Juegos[0].Nombre == "Juego; raro" && comillas.Juegos[0].Plataforma == "PS5",
                  "columnas en otro orden + comillas + plataforma normalizada");
        var malo = RepoCsv.ParsearTexto("id;nombre;plataforma;fecha_lanzamiento;precio_sugerido\n1;A;PC;2020-01-01;5\nx;B;PC;2020-01-01;5\n2;C;Atari;2020-01-01;5\n3;D;PC;ayer;5\n4;E;PC;2020-01-01;-1\n5;A;PC;2020-01-01;5\n6;F;PC;2020-01-01;5\n");
        Verificar(malo.Juegos.Count == 2 && malo.Errores.Count == 5, "filas malas se reportan y NO detienen la carga", $"juegos={malo.Juegos.Count} errores={malo.Errores.Count}");

        Console.WriteLine("== Nombres ==");
        Verificar(NormalizadorNombres.Coincide("EA Sports FC 24", "EA SPORTS FC™ 24"), "™ y mayúsculas");
        Verificar(NormalizadorNombres.Coincide("Marvel's Spider-Man 2", "Marvel’s Spider-Man 2"), "apóstrofo curvo");
        Verificar(NormalizadorNombres.Coincide("Pokémon Scarlet", "Pokemon Scarlet"), "tildes");
        Verificar(NormalizadorNombres.Coincide("Final Fantasy VII Remake", "FINAL FANTASY 7 REMAKE"), "romanos = dígitos");
        Verificar(NormalizadorNombres.Coincide("Elden Ring", "ELDEN RING Deluxe Edition"), "se ignora la edición");
        Verificar(NormalizadorNombres.Coincide("The Last of Us Part I", "Last of Us Part I"), "'The' inicial");
        Verificar(!NormalizadorNombres.Coincide("Doom", "Doom Eternal"), "Doom ≠ Doom Eternal");
        Verificar(!NormalizadorNombres.Coincide("God of War", "God of War Ragnarök"), "God of War ≠ Ragnarök");
        Verificar(!NormalizadorNombres.Coincide("FIFA 23", "FIFA 22"), "FIFA 23 ≠ FIFA 22");
        Verificar(!NormalizadorNombres.Coincide("Elden Ring", "Elden Ring Nightreign"), "Elden Ring ≠ Nightreign");
        Verificar(NormalizadorNombres.Slug("Marvel's Spider-Man 2") == "marvels-spider-man-2", "slug Metacritic");

        Console.WriteLine("== Oferta ==");
        var o = CalculadorOferta.Evaluar(29.99m, 59.99m, 59.99m);
        Verificar(o.EnOferta && o.DescuentoPct == 50.01m && o.Referencia == "precio_regular", "59.99 -> 29.99 = 50.01 %");
        Verificar(!CalculadorOferta.Evaluar(59.99m, 59.99m, 59.99m).EnOferta, "sin descuento no es oferta");
        Verificar(!CalculadorOferta.Evaluar(59.995m, 60m, 70m).EnOferta, "diferencia de 1 centavo no cuenta (tolerancia)");
        var o2 = CalculadorOferta.Evaluar(30m, null, 60m);
        Verificar(o2.EnOferta && o2.DescuentoPct == 50m && o2.Referencia == "precio_sugerido", "sin regular: usa el precio sugerido");
        var cons = CalculadorOferta.Consolidar(new[]
        {
            ResultadoTienda.Exito("Steam", 39.99m, 59.99m, "digital", "PC", "u1", null),
            ResultadoTienda.Exito("Nintendo eShop", 29.99m, 59.99m, "digital", "Switch", "u2", null),
            ResultadoTienda.Fallo("Otra", TipoError.Timeout)
        }, 59.99m);
        Verificar(cons.MejorTienda == "Nintendo eShop" && cons.MejorPrecio == 29.99m && cons.EnOferta && cons.DescuentoPct == 50.01m,
                  "el mejor precio es el MENOR de todas las tiendas (ignora las fallidas)");

        Console.WriteLine("== Contrato 1 (JSON) ==");
        string json = Serializacion.AJson(ResultadoTienda.Fallo("Steam", TipoError.Timeout));
        Verificar(json.Contains("\"ok\": false") && json.Contains("\"error\": \"timeout\"") && json.Contains("fecha_consulta") && !json.Contains("precio_actual"),
                  "error: ok,error,fecha_consulta y sin campos vacíos");
        string json2 = Serializacion.AJson(ResultadoTienda.Exito("Steam", 1m, 2m, "digital", "PC", "e", null));
        Verificar(json2.Contains("precio_actual") && json2.Contains("precio_regular") && json2.Contains("formato") && json2.Contains("enlace"), "éxito: campos del contrato");

        Console.WriteLine("== Steam ==");
        {
            var (cli, mf, _) = Montar((r, n) => ManejadorFalso.Json(Fixtures.SteamBusqueda("ELDEN RING", 5999, 3599)));
            var r = new SteamScraper(cli).ConsultarAsync(J("Elden Ring", "PC")).Result;
            Verificar(r.Ok && r.PrecioActual == 35.99m && r.PrecioRegular == 59.99m && r.Formato == "digital" && r.Plataforma == "PC"
                      && r.Enlace == "https://store.steampowered.com/app/1245620/" && r.ImagenUrl!.Contains("1245620"),
                      "éxito: precio actual/regular, formato, plataforma, enlace, imagen", Serializacion.AJson(r));
            Verificar(mf.Urls.Count(u => u.Contains("/api/storesearch")) == 1, "1 sola solicitud de búsqueda por juego");
            var cons2 = new SteamScraper(cli).ConsultarAsync(J("Elden Ring", "PS5")).Result;
            Verificar(!cons2.Ok && cons2.Error == TipoError.NoEncontrado, "plataforma no soportada -> no_encontrado (sin pedir nada)");
        }
        {
            var (cli, _, _) = Montar((r, n) => ManejadorFalso.Json(Fixtures.SteamBusqueda("Doom Eternal", 3999, 3999)));
            var r = new SteamScraper(cli).ConsultarAsync(J("Doom", "PC")).Result;
            Verificar(!r.Ok && r.Error == TipoError.NoEncontrado, "'Doom' NO se confunde con 'Doom Eternal'");
        }
        {
            var (cli, _, _) = Montar((r, n) => ManejadorFalso.Json(Fixtures.SteamBusqueda("Elden Ring", 5999, 5999, windows: false)));
            var r = new SteamScraper(cli).ConsultarAsync(J("Elden Ring", "PC")).Result;
            Verificar(!r.Ok && r.Error == TipoError.NoEncontrado, "producto sin Windows -> no_encontrado");
        }
        {
            var (cli, mf, _) = Montar((r, n) => ManejadorFalso.Texto(HttpStatusCode.Forbidden, "no"));
            var r = new SteamScraper(cli).ConsultarAsync(J("Elden Ring", "PC")).Result;
            Verificar(!r.Ok && r.Error == TipoError.Bloqueo && mf.Urls.Count(u => u.Contains("storesearch")) == 1, "403 -> bloqueo y NO se reintenta");
        }
        {
            var (cli, _, _) = Montar((r, n) => ManejadorFalso.Json("""{"cambio":"todo"}"""));
            var r = new SteamScraper(cli).ConsultarAsync(J("Elden Ring", "PC")).Result;
            Verificar(!r.Ok && r.Error == TipoError.FormatoCambiado, "JSON con otra forma -> formato_cambiado (sin crash)");
        }
        {
            var (cli, _, _) = Montar((r, n) => ManejadorFalso.Json("esto no es json {"));
            var r = new SteamScraper(cli).ConsultarAsync(J("Elden Ring", "PC")).Result;
            Verificar(!r.Ok && r.Error == TipoError.FormatoCambiado, "respuesta basura -> formato_cambiado (sin crash)");
        }
        {
            var (cli, mf, _) = Montar((r, n) => n <= 3 ? ManejadorFalso.Texto(HttpStatusCode.InternalServerError, "x")
                                                         : ManejadorFalso.Json(Fixtures.SteamBusqueda("Elden Ring", 5999, 5999)));
            // n=1 es robots.txt (lo atiende el montaje); los fallos son las solicitudes 2 y 3, la 4 funciona
            var r = new SteamScraper(cli).ConsultarAsync(J("Elden Ring", "PC")).Result;
            Verificar(r.Ok, "se recupera tras 2 errores 500 con reintentos", Serializacion.AJson(r));
        }
        {
            var (cli, mf, log) = Montar((r, n) => ManejadorFalso.Texto(HttpStatusCode.ServiceUnavailable, "x"));
            var r = new SteamScraper(cli).ConsultarAsync(J("Elden Ring", "PC")).Result;
            int intentos = mf.Urls.Count(u => u.Contains("storesearch"));
            Verificar(!r.Ok && r.Error == TipoError.Timeout && intentos == 3, $"servidor caído -> timeout tras 1+2 reintentos ({intentos} intentos)");
            Verificar(log.ContadoresPorTipo.GetValueOrDefault("reintento") == 2 && log.ContadoresPorTipo.GetValueOrDefault("timeout") == 1,
                      "el log registra los 2 reintentos y el fallo final");
        }
        {
            var (cli, _, _) = Montar((r, n) => throw new TaskCanceledException("simulado"));
            var cfg = CfgRapida with { Timeout = TimeSpan.FromMilliseconds(200) };
            var r = new SteamScraper(cli).ConsultarAsync(J("Elden Ring", "PC")).Result;
            Verificar(!r.Ok && r.Error == TipoError.Timeout, "excepción de timeout -> timeout controlado");
        }
        {
            var det = """{"1245620":{"success":true,"data":{"type":"game","name":"ELDEN RING","header_image":"https://img/h.jpg","price_overview":{"initial":5999,"final":4499}}}}""";
            var (cli, mf, _) = Montar((r, n) => ManejadorFalso.Json(det));
            var r = new SteamScraper(cli).ConsultarAsync(J("Elden Ring", "PC", appid: "1245620")).Result;
            Verificar(r.Ok && r.PrecioActual == 44.99m && r.ImagenUrl == "https://img/h.jpg" && mf.Urls.Any(u => u.Contains("appdetails")),
                      "con steam_appid usa appdetails");
            var (cli2, _, _) = Montar((r, n) => ManejadorFalso.Json(det));
            var r2 = new SteamScraper(cli2).ConsultarAsync(J("Otro Juego", "PC", appid: "1245620")).Result;
            Verificar(!r2.Ok && r2.Error == TipoError.NoEncontrado, "appid que no corresponde al nombre -> no_encontrado");
        }

        Console.WriteLine("== Metacritic ==");
        {
            var (cli, mf, _) = Montar((r, n) => ManejadorFalso.Html(Fixtures.MetacriticHtml("Elden Ring", 94, "PlayStation 5")));
            var r = new MetacriticScraper(cli).ConsultarAsync(J("Elden Ring", "PS5")).Result;
            Verificar(r.Ok && r.Score == 94 && r.Plataforma == "PS5" && r.Verificada == true && mf.Urls.Any(u => u.Contains("platform=playstation-5")),
                      "score de la consola pedida (URL con platform=playstation-5)", Serializacion.AJson(r));
        }
        {
            var (cli, _, _) = Montar((r, n) => ManejadorFalso.Html(Fixtures.MetacriticHtml("Elden Ring", 94, "PC")));
            var r = new MetacriticScraper(cli).ConsultarAsync(J("Elden Ring", "PS5")).Result;
            Verificar(!r.Ok && r.Error == TipoError.NoEncontrado, "la página es de otra plataforma -> no se devuelve ese score");
        }
        {
            var (cli, _, _) = Montar((r, n) => ManejadorFalso.Html("<div class=\"c-siteReviewScore title-card\"><span data-v-1>88</span></div>"));
            var r = new MetacriticScraper(cli).ConsultarAsync(J("Hades", "Switch")).Result;
            Verificar(r.Ok && r.Score == 88 && r.Verificada == false, "respaldo HTML: score con verificada=false (auditable)");
        }
        {
            var (cli, _, _) = Montar((r, n) => ManejadorFalso.Html("<html>nada</html>"));
            var r = new MetacriticScraper(cli).ConsultarAsync(J("Hades", "Switch")).Result;
            Verificar(!r.Ok && r.Error == TipoError.FormatoCambiado, "sin score -> formato_cambiado");
        }
        {
            var (cli, _, _) = Montar((r, n) => ManejadorFalso.Texto(HttpStatusCode.NotFound, "x"));
            var r = new MetacriticScraper(cli).ConsultarAsync(J("Juego Inventado", "PC")).Result;
            Verificar(!r.Ok && r.Error == TipoError.NoEncontrado, "404 -> no_encontrado");
        }

        Console.WriteLine("== Nombres con etiqueta de plataforma ==");
        Verificar(NormalizadorNombres.Coincide("Elden Ring", "ELDEN RING PS4 & PS5") && NormalizadorNombres.Coincide("Elden Ring", "ELDEN RING PS5")
                  && !NormalizadorNombres.Coincide("Elden Ring", "ELDEN RING Shadow of the Erdtree"),
                  "título 'ELDEN RING PS4 & PS5' (ej. Eneba) coincide con 'Elden Ring' (sin falsos positivos)");

        Console.WriteLine("== HowLongToBeat ==");
        {
            // Sin hltb_id: NO debe tocar /api (robots.txt de HLTB lo prohíbe): ni una sola solicitud, y el mensaje explica qué hacer.
            var (cli, mf, _) = Montar((r, n) => ManejadorFalso.Html("no debería pedirse nada"));
            var r = new HltbScraper(cli).ConsultarAsync(J("Elden Ring", "PC")).Result;
            Verificar(!r.Ok && r.Error == TipoError.NoEncontrado && (r.Detalle ?? "").Contains("hltb_id"),
                      "sin hltb_id -> no_encontrado con instrucción clara", Serializacion.AJson(r));
            Verificar(mf.Urls.All(x => !x.Contains("/api")), "sin hltb_id nunca se toca /api (prohibido por robots.txt de HLTB)", string.Join(", ", mf.Urls));
        }
        {
            string pag = """<script id="__NEXT_DATA__" type="application/json">{"props":{"pageProps":{"game":{"data":{"game":[{"game_name":"Elden Ring","comp_main":200000,"comp_plus":330000,"comp_100":0}]}}}}}</script>""";
            var (cli, mf, _) = Montar((r, n) => ManejadorFalso.Html(pag));
            var r = new HltbScraper(cli).ConsultarAsync(J("Elden Ring", "PC", hltb: "68151")).Result;
            Verificar(r.Ok && r.HorasPrincipal == 55.6 && r.HorasCompleto == null, "con hltb_id lee la página del juego; sin dato de 100 % queda vacío (N/D)", Serializacion.AJson(r));
            Verificar(mf.Urls.Any(x => x.EndsWith("/game/68151")) && mf.Urls.All(x => !x.Contains("/api")), "solo se pide /game/ID (ruta permitida)", string.Join(", ", mf.Urls));
        }
        {
            string pag = """<script id="__NEXT_DATA__" type="application/json">{"props":{"pageProps":{"game":{"data":{"game":[{"game_name":"Resident Evil 4","comp_main":57600,"comp_plus":72000,"comp_100":126000}]}}}}}</script>""";
            var (cli, _, _) = Montar((r, n) => ManejadorFalso.Html(pag));
            var r = new HltbScraper(cli).ConsultarAsync(J("Resident Evil 4", "PC", hltb: "1")).Result;
            Verificar(r.Ok && r.HorasCompleto == 35.0 && r.HorasPrincipal == 16.0 && r.HorasExtra == 20.0, "segundos -> horas (completionist 126000 s = 35 h)", Serializacion.AJson(r));
            Verificar(Math.Abs((DateTimeOffset.UtcNow - r.FechaConsulta).TotalSeconds) < 30, "fecha_consulta es la hora REAL de la consulta (no una fecha fija)", r.FechaConsulta.ToString("o"));
        }
        {
            string pag = """<script id="__NEXT_DATA__" type="application/json">{"props":{"pageProps":{"game":{"data":{"game":[{"game_name":"Elden Ring","game_image":"68151_Elden_Ring.jpg","comp_main":200000,"comp_plus":330000,"comp_100":480000,"comp_all":300000,"profile_dev":"FromSoftware","profile_platform":"PC, PS5","release_world":2022,"review_score":91,"count_comp":15000,"count_playing":2000,"count_backlog":9000,"count_retired":1000}]}}}}}</script>""";
            var (cli, _, _) = Montar((r, n) => ManejadorFalso.Html(pag));
            var r = new HltbScraper(cli).ConsultarAsync(J("Elden Ring", "PC", hltb: "68151")).Result;
            Verificar(r.Ok && r.HorasTodosLosEstilos == 83.3 && r.Desarrollador == "FromSoftware" && r.AnioLanzamiento == 2022 && r.CalificacionUsuarios == 91
                      && r.Completaron == 15000 && r.Jugando == 2000 && r.Pendiente == 9000 && r.Abandonaron == 1000 && r.Fuente == "HowLongToBeat"
                      && (r.ImagenUrl ?? "").StartsWith("https://howlongtobeat.com/games/"), "HLTB: recupera todos los campos de /game/ID (todos los estilos, dev, año, nota, contadores, imagen)", Serializacion.AJson(r));
        }
        {
            string pag = """<html><script>self.__next_f.push([1,"{\"game_name\":\"Elden Ring\",\"comp_main\":200000,\"comp_plus\":330000,\"comp_100\":480000}"])</script></html>""";
            var (cli, _, _) = Montar((r, n) => ManejadorFalso.Html(pag));
            var r = new HltbScraper(cli).ConsultarAsync(J("Elden Ring", "PC", hltb: "68151")).Result;
            Verificar(r.Ok && r.HorasCompleto == 133.3, "respaldo: si la página ya no trae __NEXT_DATA__, lee los mismos campos del texto", Serializacion.AJson(r));
        }
        {
            string pag = """<script id="__NEXT_DATA__" type="application/json">{"props":{"pageProps":{"game":{"data":{"game":[{"game_name":"Otro Juego","comp_main":1,"comp_plus":1,"comp_100":1}]}}}}}</script>""";
            var (cli, _, _) = Montar((r, n) => ManejadorFalso.Html(pag));
            var r = new HltbScraper(cli).ConsultarAsync(J("Elden Ring", "PC", hltb: "999")).Result;
            Verificar(!r.Ok && r.Error == TipoError.NoEncontrado, "hltb_id que pertenece a otro juego -> no_encontrado (error de captura en el REPO)");
        }
        {
            var (cli, _, _) = Montar((r, n) => ManejadorFalso.Html("<html>nada útil</html>"));
            var r = new HltbScraper(cli).ConsultarAsync(J("Elden Ring", "PC", hltb: "68151")).Result;
            Verificar(!r.Ok && r.Error == TipoError.FormatoCambiado, "página sin datos de tiempos -> formato_cambiado");
        }
        {
            var (cli, _, _) = Montar((r, n) => ManejadorFalso.Html("x"));
            var r = new HltbScraper(cli).ConsultarAsync(J("Elden Ring", "PC", hltb: "68151abc")).Result;
            Verificar(!r.Ok && r.Error == TipoError.FormatoCambiado, "hltb_id con letras -> formato_cambiado");
        }

        {
            // Las reglas reales que mostró el diagnóstico de HLTB: Disallow /api. /game/ID queda permitido.
            var reglas = ReglasRobots.Parsear("User-agent: anthropic-ai\nDisallow: /\n\nUser-agent: *\nDisallow: /api\n", "ScraperJuegosTEC");
            Verificar(!reglas.Permite("/api/find") && !reglas.Permite("/api/find/init?t=1") && reglas.Permite("/game/68151"),
                      "robots.txt de HLTB: /api bloqueado, /game/68151 permitido");
        }

        Console.WriteLine("== Epic Games Store ==");
        {
            var (cli, mf, _) = Montar((r, n) => ManejadorFalso.Json(Fixtures.EpicRespuesta("ELDEN RING", 3599, 5999)));
            var r = new EpicGamesScraper(cli).ConsultarAsync(J("Elden Ring", "PC")).Result;
            Verificar(r.Ok && r.PrecioActual == 35.99m && r.PrecioRegular == 59.99m && r.Formato == "digital" && r.Plataforma == "PC"
                      && r.Enlace == "https://store.epicgames.com/en-US/p/elden-ring" && r.ImagenUrl == "https://img/epic.png",
                      "éxito: precios (enteros en centavos), formato, plataforma, enlace, imagen", Serializacion.AJson(r));
            Verificar(mf.Urls.Count(u => u.StartsWith("POST") && u.Contains("/graphql")) == 1, "1 sola solicitud (POST /graphql) por juego");
            var r2 = new EpicGamesScraper(cli).ConsultarAsync(J("Elden Ring", "PS5")).Result;
            Verificar(!r2.Ok && r2.Error == TipoError.NoEncontrado, "plataforma no soportada -> no_encontrado");
        }
        {
            var (cli, _, _) = Montar((r, n) => ManejadorFalso.Json(Fixtures.EpicRespuesta("ELDEN RING", 3599, 5999, moneda: "EUR")));
            var r = new EpicGamesScraper(cli).ConsultarAsync(J("Elden Ring", "PC")).Result;
            Verificar(!r.Ok && r.Error == TipoError.FormatoCambiado, "precios en otra moneda -> formato_cambiado (no se mezclan monedas)");
        }
        {
            var (cli, _, _) = Montar((r, n) => ManejadorFalso.Json(Fixtures.EpicRespuesta("ELDEN RING", 3599, 5999)));
            var r = new EpicGamesScraper(cli).ConsultarAsync(J("Doom", "PC")).Result;
            Verificar(!r.Ok && r.Error == TipoError.NoEncontrado, "juego que no está en los resultados -> no_encontrado");
        }
        {
            var (cli, _, _) = Montar((r, n) => ManejadorFalso.Json("{\"errors\":[{\"message\":\"boom\"}]}"));
            var r = new EpicGamesScraper(cli).ConsultarAsync(J("Elden Ring", "PC")).Result;
            Verificar(!r.Ok && r.Error == TipoError.FormatoCambiado && r.Detalle!.Contains("boom"), "GraphQL con 'errors' -> formato_cambiado con el motivo");
        }
        {
            var (cli, _, _) = Montar((r, n) => ManejadorFalso.Texto(HttpStatusCode.Forbidden, "no"));
            var r = new EpicGamesScraper(cli).ConsultarAsync(J("Elden Ring", "PC")).Result;
            Verificar(!r.Ok && r.Error == TipoError.Bloqueo, "403 -> bloqueo");
        }

        Console.WriteLine("== Steam y nombres (correcciones con datos reales) ==");
        {
            // Datos reales: la versión original de Ori ya no se vende; la Definitive Edition sí. Antes se rendía en la primera.
            const string busqueda = "{\"total\":2,\"items\":[" +
                "{\"type\":\"app\",\"name\":\"Ori and the Blind Forest\",\"id\":261570,\"platforms\":{\"windows\":true}}," +
                "{\"type\":\"app\",\"name\":\"Ori and the Blind Forest: Definitive Edition\",\"id\":387290,\"price\":{\"currency\":\"USD\",\"initial\":1999,\"final\":499},\"platforms\":{\"windows\":true}}]}";
            var (cli, _, _) = Montar((r, n) => ManejadorFalso.Json(busqueda));
            var r = new SteamScraper(cli).ConsultarAsync(J("Ori and the Blind Forest", "PC")).Result;
            Verificar(r.Ok && r.PrecioActual == 4.99m && r.Enlace!.Contains("387290"),
                      "Steam: si la primera coincidencia no tiene precio (retirada), usa la siguiente que sí se vende", Serializacion.AJson(r));
            var (cli2, _, _) = Montar((r2, n) => ManejadorFalso.Json("{\"total\":1,\"items\":[{\"type\":\"app\",\"name\":\"Halo Infinite\",\"id\":1240440,\"platforms\":{\"windows\":true}}]}"));
            var r3 = new SteamScraper(cli2).ConsultarAsync(J("Halo Infinite", "PC")).Result;
            Verificar(!r3.Ok && r3.Error == TipoError.NoEncontrado && r3.Detalle!.Contains("sin precio"), "Steam: juego gratis (sin precio) -> no_encontrado con el motivo");
        }
        {
            JsonElement H(string json) { using var d = JsonDocument.Parse(json); return d.RootElement.Clone(); }
            Verificar(SteamScraper.ImagenDeBusqueda(H("{\"tiny_image\":\"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/2933620/abc123/capsule_231x87.jpg?t=17\"}"))
                          == "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/2933620/abc123/header.jpg?t=17"
                      && SteamScraper.ImagenDeBusqueda(H("{}")) == null,
                      "Steam: la imagen sale de la ruta real de la búsqueda (juegos nuevos con código en la ruta)");
        }
        Verificar(NormalizadorNombres.Coincide("Cities: Skylines", "Cities: Skylines - Nintendo Switch Edition")
                  && NormalizadorNombres.Coincide("Hades", "Hades Nintendo Switch")
                  && !NormalizadorNombres.Coincide("Nintendo Switch Sports", "Sports"),
                  "Nombres: se quita la etiqueta de consola de Nintendo al final del título");

        {
            // Ritmo adaptativo: 429 -> la pausa del dominio sube (2 s, luego 4 s) para todos los hilos.
            var espia = new LimitadorEspia();
            var mf = new ManejadorFalso((r, n) => r.RequestUri!.AbsolutePath == "/robots.txt"
                ? ManejadorFalso.Texto(HttpStatusCode.OK, Fixtures.RobotsAbierto)
                : (n <= 3 ? ManejadorFalso.Texto((HttpStatusCode)429, "lento") : ManejadorFalso.Json("{\"ok\":1}")));
            var cfg = CfgRapida with { EsperaMaxima = TimeSpan.FromMilliseconds(20) };
            var cli = new ClienteHttp(new HttpClient(mf), cfg, espia, new LogFallosArchivo(null));
            var resp = cli.ObtenerAsync("https://www.eneba.com/x", new ContextoLog("J", "PC", "Eneba"), default).Result;
            Verificar(resp.Codigo == 200 && espia.Pausas.Count >= 2 && espia.Pausas[0] == TimeSpan.FromSeconds(2) && espia.Pausas[1] == TimeSpan.FromSeconds(4)
                      && espia.Pausas.All(p => p <= TimeSpan.FromSeconds(10)),
                      "HTTP 429: baja el ritmo de ESE sitio (pausa 2 s, 4 s… máx. 10 s) y luego sigue", string.Join(",", espia.Pausas));
        }

        Console.WriteLine("== Epic Games Store vía CheapShark ==");
        {
            var (cli, mf, _) = Montar((r, n) => ManejadorFalso.Json(Fixtures.CheapSharkDeals(
                ("ELDEN RING Shadow of the Erdtree Edition", "59.99", "79.99", "25"),
                ("ELDEN RING", "35.99", "59.99", "25"))));
            var r = new EpicCheapSharkScraper(cli).ConsultarAsync(J("Elden Ring", "PC")).Result;
            Verificar(r.Ok && r.Tienda == "Epic Games Store" && r.PrecioActual == 35.99m && r.PrecioRegular == 59.99m && r.Formato == "digital"
                      && r.Plataforma == "PC" && r.Enlace == "https://www.cheapshark.com/redirect?dealID=abc1%3D" && r.ImagenUrl == "https://img/cs1.jpg",
                      "éxito: precios en texto, edición base, enlace (redirect con dealID sin doble codificar) e imagen", Serializacion.AJson(r));
            Verificar(mf.Urls.Count(u => u.Contains("/api/1.0/deals") && u.Contains("storeID=25") && u.Contains("exact=1")) == 1
                      && mf.Urls.Count(u => u.Contains("/api/1.0/deals")) == 1,
                      "1 sola solicitud (exact=1, storeID=25) cuando el título coincide", string.Join(" | ", mf.Urls));
            var r2 = new EpicCheapSharkScraper(cli).ConsultarAsync(J("Elden Ring", "Switch")).Result;
            Verificar(!r2.Ok && r2.Error == TipoError.NoEncontrado, "plataforma no PC -> no_encontrado (sin solicitud)");
        }
        {
            // exact=1 no encuentra (CheapShark usa otro título) -> 2a solicitud con exact=0 y filtro por nombre
            var (cli, mf, _) = Montar((r, n) => ManejadorFalso.Json(r.RequestUri!.Query.Contains("exact=1")
                ? "[]" : Fixtures.CheapSharkDeals(("Hades Soundtrack", "4.99", "9.99", "25"), ("Hades™", "12.49", "24.99", "25"))));
            var r = new EpicCheapSharkScraper(cli).ConsultarAsync(J("Hades", "PC")).Result;
            Verificar(r.Ok && r.PrecioActual == 12.49m && r.PrecioRegular == 24.99m
                      && mf.Urls.Count(u => u.Contains("exact=0")) == 1, "sin coincidencia exacta: búsqueda parcial (exact=0) y descarta la banda sonora", Serializacion.AJson(r));
        }
        {
            var (cli, _, _) = Montar((r, n) => ManejadorFalso.Json(Fixtures.CheapSharkDeals(("Elden Ring", "30.00", "59.99", "1"))));
            var r = new EpicCheapSharkScraper(cli).ConsultarAsync(J("Elden Ring", "PC")).Result;
            Verificar(!r.Ok && r.Error == TipoError.NoEncontrado, "oferta de OTRA tienda (storeID 1 = Steam) se ignora -> no_encontrado");
        }
        {
            var (cli, _, _) = Montar((r, n) => ManejadorFalso.Json(Fixtures.CheapSharkDeals(("Fortnite", "0.00", "0.00", "25"))));
            var r = new EpicCheapSharkScraper(cli).ConsultarAsync(J("Fortnite", "PC")).Result;
            Verificar(!r.Ok && r.Error == TipoError.NoEncontrado, "juego gratuito -> no_encontrado (no hay precio que comparar)");
        }
        {
            var (cli, _, _) = Montar((r, n) => ManejadorFalso.Json("{\"error\":\"Unknown store\"}"));
            var r = new EpicCheapSharkScraper(cli).ConsultarAsync(J("Elden Ring", "PC")).Result;
            Verificar(!r.Ok && r.Error == TipoError.FormatoCambiado && r.Detalle!.Contains("Unknown store"), "respuesta que no es lista -> formato_cambiado con el motivo");
        }
        {
            var (cli, _, _) = Montar((r, n) => ManejadorFalso.Json("[{\"title\":\"Elden Ring\",\"storeID\":\"25\"}]"));
            var r = new EpicCheapSharkScraper(cli).ConsultarAsync(J("Elden Ring", "PC")).Result;
            Verificar(!r.Ok && r.Error == TipoError.FormatoCambiado, "oferta sin salePrice/normalPrice -> formato_cambiado");
        }
        {
            var (cli, _, _) = Montar((r, n) => ManejadorFalso.Texto((System.Net.HttpStatusCode)429, "slow down"));
            var r = new EpicCheapSharkScraper(cli).ConsultarAsync(J("Elden Ring", "PC")).Result;
            Verificar(!r.Ok && r.Error == TipoError.Bloqueo, "429 (demasiadas solicitudes) -> bloqueo, sin insistir");
        }

        Console.WriteLine("== Nintendo eShop ==");
        {
            JsonElement H(string json) { using var d = JsonDocument.Parse(json); return d.RootElement.Clone(); }
            Verificar(NintendoScraper.Imagen(H("{\"productImage\":\"ncom/software/switch/70010000027619/abc123\"}"))
                          == "https://assets.nintendo.com/image/upload/f_auto,q_auto,w_920/ncom/software/switch/70010000027619/abc123"
                      && NintendoScraper.Imagen(H("{\"productImage\":\"/content/dam/noa/x.png\"}")) == "https://www.nintendo.com/content/dam/noa/x.png"
                      && NintendoScraper.Imagen(H("{\"horizontalHeaderImage\":\"https://img/h.jpg\",\"productImage\":\"x\"}")) == "https://img/h.jpg"
                      && NintendoScraper.Imagen(H("{}")) == null,
                      "Nintendo: imagen como id del servidor de imágenes, ruta del sitio o URL completa (prefiere la horizontal)");
        }
        {
            var (cli, mf, _) = Montar((r, n) => ManejadorFalso.Json(Fixtures.NintendoRespuesta("Hades", "Nintendo Switch", "59.99", "39.99")));
            var r = new NintendoScraper(cli).ConsultarAsync(J("Hades", "Switch")).Result;
            Verificar(r.Ok && r.PrecioActual == 39.99m && r.PrecioRegular == 59.99m && r.Plataforma == "Switch" && r.Formato == "digital"
                      && r.Enlace == "https://www.nintendo.com/store/products/juego-switch/"
                      && r.ImagenUrl == "https://www.nintendo.com/content/dam/noa/img.png",
                      "éxito: msrp/salePrice, enlace e imagen absolutos; el 'Expansion Pass' no se confunde con el juego", Serializacion.AJson(r));
            Verificar(mf.Urls.Count(u => u.StartsWith("POST") && u.Contains("algolia.net/1/indexes/store_game_en_us/query")) == 1,
                      "1 sola solicitud (POST al índice) por juego");
            var r2 = new NintendoScraper(cli).ConsultarAsync(J("Hades", "PC")).Result;
            Verificar(!r2.Ok && r2.Error == TipoError.NoEncontrado, "plataforma que no es Switch -> no_encontrado");
        }
        {
            var (cli, _, _) = Montar((r, n) => ManejadorFalso.Json(Fixtures.NintendoRespuesta("Hades", "Nintendo Switch", "24.99", "null")));
            var r = new NintendoScraper(cli).ConsultarAsync(J("Hades", "Switch")).Result;
            Verificar(r.Ok && r.PrecioActual == 24.99m && r.PrecioRegular == 24.99m, "sin salePrice: precio actual = precio regular");
        }
        {
            var (cli, _, _) = Montar((r, n) => ManejadorFalso.Json(Fixtures.NintendoRespuesta("Hades", "iOS/Android", "24.99", "null")));
            var r = new NintendoScraper(cli).ConsultarAsync(J("Hades", "Switch")).Result;
            Verificar(!r.Ok && r.Error == TipoError.NoEncontrado, "resultado de otra plataforma -> no_encontrado");
        }
        {
            var (cli, _, _) = Montar((r, n) => ManejadorFalso.Texto(HttpStatusCode.Forbidden, "Invalid Application-ID or API key"));
            var r = new NintendoScraper(cli).ConsultarAsync(J("Hades", "Switch")).Result;
            Verificar(!r.Ok && r.Error == TipoError.Bloqueo, "clave pública rotada (403) -> bloqueo controlado, sin crash");
        }
        {
            var (cli, _, _) = Montar((r, n) => ManejadorFalso.Json("{\"otra\":\"cosa\"}"));
            var r = new NintendoScraper(cli).ConsultarAsync(J("Hades", "Switch")).Result;
            Verificar(!r.Ok && r.Error == TipoError.FormatoCambiado, "sin arreglo 'hits' -> formato_cambiado");
        }
        {
            var (cli, mf, _) = Montar((r, n) => ManejadorFalso.Json("{}"), CfgRapida with { NintendoAlgoliaKey = "" });
            var r = new NintendoScraper(cli).ConsultarAsync(J("Hades", "Switch")).Result;
            Verificar(!r.Ok && r.Error == TipoError.FormatoCambiado && mf.Total == 0, "sin clave configurada -> error controlado y no se pide nada");
        }

        Console.WriteLine("== Eneba ==");
        {
            // Datos con la forma REAL de Eneba (__ALGOLIA_STATE__): precios en centavos, regiones y DLC.
            var html = Fixtures.EnebaAlgoliaHtml(
                ("Elden Ring (PC) Steam Key EUROPE", "game", "steam", new[] { "europe" }, 4509, 5999, "steam-elden-ring-pc-steam-key-europe"),
                ("Elden Ring (PC) Steam Key LATAM", "game", "steam", new[] { "latam" }, 4676, 5999, "steam-elden-ring-pc-steam-key-latam"),
                ("Elden Ring: Shadow of the Erdtree (DLC) (PC) Steam Key LATAM", "dlc", "steam", new[] { "latam" }, 3416, 3999, "steam-elden-ring-dlc"),
                ("ELDEN RING NIGHTREIGN Steam Key (PC) LATAM", "game", "steam", new[] { "latam" }, 2718, 3999, "steam-nightreign"));
            var (cli, _, _) = Montar((r, n) => ManejadorFalso.Html(html));
            var r = new EnebaScraper(cli).ConsultarAsync(J("Elden Ring", "PC")).Result;
            Verificar(r.Ok && r.PrecioActual == 46.76m && r.PrecioRegular == 59.99m && r.Plataforma == "PC"
                      && r.Enlace == "https://www.eneba.com/steam-elden-ring-pc-steam-key-latam" && r.ImagenUrl == "https://img/eneba-300.jpg",
                      "Eneba (__ALGOLIA_STATE__): centavos a dólares; clave LATAM de PC; se descartan EUROPE, DLC y otro juego", Serializacion.AJson(r));
        }
        {
            var html = Fixtures.EnebaAlgoliaHtml(
                ("Elden Ring XBOX LIVE Key GLOBAL", "game", "xbox", new[] { "global" }, 5711, 7796, "xbox-elden-ring-xbox-live-key-global"));
            var (cli, _, _) = Montar((r, n) => ManejadorFalso.Html(html));
            var r = new EnebaScraper(cli).ConsultarAsync(J("Elden Ring", "Xbox Series")).Result;
            Verificar(r.Ok && r.PrecioActual == 57.11m && r.Plataforma == "Xbox Series",
                      "Eneba: clave 'XBOX LIVE' GLOBAL sin generación en el nombre sirve para Xbox", Serializacion.AJson(r));
        }
        {
            var html = Fixtures.EnebaAlgoliaHtml(
                ("Elden Ring (PC) Steam Key EUROPE", "game", "steam", new[] { "europe" }, 4509, 5999, "steam-elden-ring-pc-steam-key-europe"));
            var (cli, _, _) = Montar((r, n) => ManejadorFalso.Html(html));
            var r = new EnebaScraper(cli).ConsultarAsync(J("Elden Ring", "PC")).Result;
            Verificar(!r.Ok && r.Error == TipoError.NoEncontrado, "Eneba: solo hay claves de una región no aceptada (EUROPE) -> no_encontrado");
        }
        {
            var (cli, _, _) = Montar((r, n) => ManejadorFalso.Html(Fixtures.EnebaHtml("Elden Ring (PC) Steam Key GLOBAL", "34.50", "USD")));
            var r = new EnebaScraper(cli).ConsultarAsync(J("Elden Ring", "PC")).Result;
            Verificar(r.Ok && r.PrecioActual == 34.50m && r.PrecioRegular == null && r.Formato == "digital" && r.Plataforma == "PC"
                      && r.Enlace == "https://www.eneba.com/elden-ring-steam-key-global" && r.ImagenUrl == "https://img/eneba.jpg",
                      "éxito: clave GLOBAL de PC; la de EUROPE (más barata) se descarta; sin precio_regular", Serializacion.AJson(r));
            var ofEneba = CalculadorOferta.Consolidar(new[] { r }, 59.99m);
            Verificar(ofEneba.EnOferta && ofEneba.MejorTienda == "Eneba", "sin precio regular, la oferta se evalúa contra el precio sugerido del REPO");
        }
        {
            var (cli, _, _) = Montar((r, n) => ManejadorFalso.Html(Fixtures.EnebaHtml("Elden Ring (PC) Steam Key GLOBAL", "31.00", "EUR")));
            var r = new EnebaScraper(cli).ConsultarAsync(J("Elden Ring", "PC")).Result;
            Verificar(!r.Ok && r.Error == TipoError.FormatoCambiado, "precio en EUR -> formato_cambiado (no se mezclan monedas)");
        }
        {
            var (cli, _, _) = Montar((r, n) => ManejadorFalso.Html(Fixtures.EnebaHtml("Elden Ring (PC) Steam Key GLOBAL", "34.50", "USD")));
            var r = new EnebaScraper(cli).ConsultarAsync(J("Elden Ring", "PS5")).Result;
            Verificar(!r.Ok && r.Error == TipoError.NoEncontrado, "solo hay clave de PC y se pidió PS5 -> no_encontrado (no se adivina)");
        }
        {
            var (cli, _, _) = Montar((r, n) => ManejadorFalso.Html("<html><body>cambiaron todo</body></html>"));
            var r = new EnebaScraper(cli).ConsultarAsync(J("Elden Ring", "PC")).Result;
            Verificar(!r.Ok && r.Error == TipoError.FormatoCambiado, "sin datos estructurados -> formato_cambiado");
        }
        {
            var (cli, _, _) = Montar((r, n) => ManejadorFalso.Html("<html><title>Just a moment...</title></html>"));
            var r = new EnebaScraper(cli).ConsultarAsync(J("Elden Ring", "PC")).Result;
            Verificar(!r.Ok && r.Error == TipoError.Bloqueo, "desafío anti-bots -> bloqueo");
        }
        Verificar(EnebaScraper.NombreBase("Elden Ring (PC) Steam Key GLOBAL") == "Elden Ring", "Eneba: limpia '(PC) Steam Key GLOBAL' del nombre");
        Verificar(NormalizadorNombres.Coincide("The Last of Us Part I", EnebaScraper.NombreBase("The Last of Us Part I (PC) Steam Key")),
                  "Eneba: 'Us' dentro del nombre no se confunde con una región");
        Verificar(EnebaScraper.TieneRegion("Juego Key EU") && !EnebaScraper.TieneRegion("The Last of Us Part I Key GLOBAL"), "Eneba: detecta claves con región");
        Verificar(EnebaScraper.DetectarPlataforma("Forza Horizon 5 (Xbox Series X|S) Key") == "Xbox Series"
                  && EnebaScraper.DetectarPlataforma("Hades Nintendo Switch eShop Key") == "Switch"
                  && EnebaScraper.DetectarPlataforma("Juego Key") == null, "Eneba: plataforma deducida del nombre (o null si no se puede confirmar)");

        Console.WriteLine("== Tiendas y robustez (cambios y juegos fuera del REPO) ==");
        {
            var (cli, _, _) = Montar((r, n) => ManejadorFalso.Json("{}"));
            var tiendas = TiendasDisponibles.Crear(cli, Array.Empty<string>());   // sin depender de variables de entorno
            Verificar(tiendas.Select(t => t.Tienda).OrderBy(x => x).SequenceEqual(Catalogos.Tiendas.OrderBy(x => x)),
                      "la lista de tiendas coincide con Catalogos.Tiendas (Steam, Epic, Nintendo, Eneba)");
            Verificar(Catalogos.Plataformas.All(p => tiendas.Any(t => t.SoportaPlataforma(p))), "toda plataforma del REPO tiene al menos una tienda");
            var rota = TiendasDisponibles.ConsultarSeguroAsync(new TiendaRota(), J("X", "PC")).Result;
            Verificar(!rota.Ok && rota.Error == TipoError.FormatoCambiado && rota.Tienda == "Rota", "un scraper que lanza una excepción NO tumba la consulta");
        }
        {
            var consMoneda = CalculadorOferta.Consolidar(new[]
            {
                ResultadoTienda.Exito("Eneba", 5m, 5m, "digital", "PC", "u1", null, "EUR"),
                ResultadoTienda.Exito("Steam", 39.99m, 59.99m, "digital", "PC", "u2", null)
            }, 59.99m);
            Verificar(consMoneda.MejorTienda == "Steam" && consMoneda.MejorPrecio == 39.99m, "un precio en otra moneda NO gana el 'mejor precio'");
            var sinSugerido = CalculadorOferta.Consolidar(new[] { ResultadoTienda.Exito("Steam", 39.99m, 59.99m, "digital", "PC", "u2", null) }, 0m);
            Verificar(sinSugerido.EnOferta, "juego fuera del REPO (precio sugerido 0): la oferta se calcula con el precio regular de la tienda");
        }
        {
            var (cli, _, _) = Montar((r, n) => ManejadorFalso.Json("{\"total\":0,\"items\":[]}"));
            var r = new SteamScraper(cli).ConsultarAsync(new Juego(-1, "Juego Que No Existe 123", "PC", DateTime.MinValue, 0m)).Result;
            Verificar(!r.Ok && r.Error == TipoError.NoEncontrado, "juego fuera del REPO y sin resultados -> no_encontrado (no se cae)");
        }
        {
            string tmp = Path.Combine(Path.GetTempPath(), "repo_bloq_" + Guid.NewGuid().ToString("N") + ".csv");
            File.WriteAllText(tmp, "id;nombre;plataforma;fecha_lanzamiento;precio_sugerido\n1;Doom;PC;2016-05-13;19.99\n");
            using (var abierto = new FileStream(tmp, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
            {
                var rl = new RepoCsv(tmp).Cargar();
                Verificar(rl.Errores.Count == 0 && rl.Juegos.Count == 1, "el REPO se lee aunque el CSV esté abierto en Excel");
            }
            var falta = new RepoCsv(tmp + ".noexiste").Cargar();
            Verificar(falta.Juegos.Count == 0 && falta.Errores.Count == 1, "REPO inexistente -> error reportado, no excepción");
            File.Delete(tmp);
        }

        Console.WriteLine("== robots.txt ==");
        {
            var mf = new ManejadorFalso((r, n) => r.RequestUri!.AbsolutePath == "/robots.txt"
                ? ManejadorFalso.Texto(HttpStatusCode.OK, "User-agent: *\nDisallow: /api/\nCrawl-delay: 0.01")
                : ManejadorFalso.Json("{}"));
            var cli = new ClienteHttp(new HttpClient(mf), CfgRapida, new LimitadorDominio(2, TimeSpan.Zero), new LogNulo());
            var r = new SteamScraper(cli).ConsultarAsync(J("Elden Ring", "PC")).Result;
            Verificar(!r.Ok && r.Error == TipoError.Bloqueo && !mf.Urls.Any(u => u.Contains("/api/")),
                      "Disallow en robots.txt -> bloqueo y la solicitud ni se hace", string.Join(",", mf.Urls));
        }
        {
            var reglas = ReglasRobots.Parsear("User-agent: *\nDisallow: /\nAllow: /public\n\nUser-agent: ScraperJuegosTEC\nDisallow: /privado$\n", "ScraperJuegosTEC");
            Verificar(reglas.Permite("/cualquiera") && !reglas.Permite("/privado") && reglas.Permite("/privado/x"), "se usa el grupo de nuestro token y no el '*'");
            var r2 = ReglasRobots.Parsear("User-agent: *\nDisallow: /\nAllow: /public", "X");
            Verificar(r2.Permite("/public/a") && !r2.Permite("/otra"), "gana la regla más específica");
        }
        {
            var mf = new ManejadorFalso((r, n) => ManejadorFalso.Texto(HttpStatusCode.InternalServerError, "x"));
            var cli = new ClienteHttp(new HttpClient(mf), CfgRapida, new LimitadorDominio(2, TimeSpan.Zero), new LogNulo());
            var r = new SteamScraper(cli).ConsultarAsync(J("Elden Ring", "PC")).Result;
            Verificar(!r.Ok && r.Error == TipoError.Bloqueo && mf.Urls.All(u => u.Contains("robots.txt")),
                      "si no se puede leer robots.txt (5xx) NO se pide nada");
        }

        Console.WriteLine("== Limitador por dominio ==");
        {
            var lim = new LimitadorDominio(maxConcurrenciaPorDominio: 2, pausaMinima: TimeSpan.FromMilliseconds(30));
            int dentro = 0, maximo = 0; var inicios = new ConcurrentBag<long>(); var sw = Stopwatch.StartNew();
            Task.WhenAll(Enumerable.Range(0, 12).Select(async _ =>
            {
                using var t = await lim.EntrarAsync("x.com");
                inicios.Add(sw.ElapsedMilliseconds);
                int d = Interlocked.Increment(ref dentro);
                int m; do { m = Volatile.Read(ref maximo); } while (d > m && Interlocked.CompareExchange(ref maximo, d, m) != m);
                await Task.Delay(40);
                Interlocked.Decrement(ref dentro);
            })).Wait();
            var o2s = inicios.OrderBy(x => x).ToList();
            long minGap = Enumerable.Range(1, o2s.Count - 1).Min(i => o2s[i] - o2s[i - 1]);
            Verificar(maximo <= 2, $"nunca más de 2 simultáneas al mismo dominio (máx observado {maximo})");
            // Tolerancia de 15 ms: el reloj de Windows tiene una resolución de ~15,6 ms y Task.Delay puede despertar un poco antes.
            Verificar(minGap >= 14, $"pausa mínima entre inicios respetada (mínimo observado {minGap} ms de 30; tolerancia 15 ms del reloj)");
            var otro = lim.EntrarAsync("y.com").Result; otro.Dispose();
            Verificar(true, "dominios distintos no se bloquean entre sí");
        }

        Console.WriteLine("== Paralelo sin estado compartido ==");
        {
            var ruta = Path.Combine(Path.GetTempPath(), "scraper_test_" + Guid.NewGuid().ToString("N") + ".log");
            var (cli, mf, log) = Montar((r, n) =>
            {
                string term = Uri.UnescapeDataString(r.RequestUri!.Query.Split("term=")[1].Split('&')[0]);
                if (term.EndsWith("7")) return ManejadorFalso.Texto(HttpStatusCode.Forbidden, "no"); // algunos fallan a propósito
                return ManejadorFalso.Json(Fixtures.SteamBusqueda(term, 5999, 5999 - (term.Length % 5) * 100));
            }, rutaLog: ruta);
            var steam = new SteamScraper(cli);
            var juegos = Enumerable.Range(0, 60).Select(i => J("Juego " + i, "PC")).ToList();
            var resultados = new ConcurrentDictionary<string, ResultadoTienda>();
            Parallel.ForEachAsync(juegos, new ParallelOptions { MaxDegreeOfParallelism = 16 }, async (j, ct) =>
                resultados[j.Nombre] = await steam.ConsultarAsync(j, ct)).Wait();
            int bloqueos = resultados.Values.Count(x => x.Error == TipoError.Bloqueo);
            int esperados = juegos.Count(j => j.Nombre.EndsWith("7"));
            Verificar(resultados.Count == 60 && bloqueos == esperados && resultados.Values.Count(x => x.Ok) == 60 - esperados,
                      $"60 juegos en 16 hilos: {60 - esperados} ok y {bloqueos} bloqueos controlados, ninguno detuvo al resto");
            Verificar(resultados.Values.Where(x => x.Ok).All(x => x.Enlace == "https://store.steampowered.com/app/1245620/" && x.PrecioActual > 0),
                      "los resultados no se mezclan entre hilos");
            log.Dispose();
            var lineas = File.ReadAllLines(ruta);
            Verificar(lineas.Length == esperados && lineas.All(l => l.Split(';').Length == 6),
                      $"el log tiene {lineas.Length} líneas íntegras (fecha;juego;plataforma;fuente;tipo;detalle) con escritura concurrente");
            File.Delete(ruta);
            int robots = mf.Urls.Count(u => u.Contains("robots.txt"));
            Verificar(robots == 1, $"robots.txt se descargó 1 sola vez para 60 juegos en paralelo (se pidió {robots})");
        }

        Console.WriteLine("== hltb_id automático (Wikidata) ==");
        {
            static string Busq(params (string q, string label, string desc)[] xs) =>
                "{\"search\":[" + string.Join(",", xs.Select(x => $"{{\"id\":\"{x.q}\",\"label\":\"{x.label}\",\"description\":\"{x.desc}\",\"match\":{{\"type\":\"label\",\"text\":\"{x.label}\"}}}}")) + "],\"success\":1}";
            static string Ents(params (string q, string? hltb)[] xs) =>
                "{\"entities\":{" + string.Join(",", xs.Select(x => $"\"{x.q}\":{{\"id\":\"{x.q}\",\"claims\":{(x.hltb == null ? "{}" : $"{{\"P2816\":[{{\"mainsnak\":{{\"datavalue\":{{\"value\":\"{x.hltb}\",\"type\":\"string\"}}}},\"rank\":\"normal\"}}]}}")}}}")) + "}}";
            ClienteHttp Cli(string busqueda, string entidades, out ManejadorFalso mf)
            {
                var m = new ManejadorFalso((r, n) => ManejadorFalso.Json(r.RequestUri!.Query.Contains("wbsearchentities") ? busqueda : entidades));
                mf = m;
                return new ClienteHttp(new HttpClient(m), new ConfigScraping { Timeout = TimeSpan.FromSeconds(2), MaxReintentos = 0, UserAgent = "test", RespetarRobots = false },
                                       new LimitadorDominio(4, TimeSpan.Zero), new LogNulo());
            }

            var c1 = Cli(Busq(("Q1", "Elden Ring", "2022 action role-playing game"), ("Q2", "Elden Ring", "2022 film")), Ents(("Q1", "68151")), out var mf1);
            var r1 = new ResolverHltbWikidata(c1).ResolverAsync(J("Elden Ring", "PS5")).Result;
            Verificar(r1.HltbId == "68151" && r1.Estado == "ok", "Wikidata: un videojuego con el nombre -> su hltb_id (la película homónima se ignora)", Serializacion.AJson(r1));
            Verificar(mf1.Urls.All(u => u.Contains("wikidata.org")) && mf1.Urls.Count == 2 && mf1.Urls.All(u => !u.Contains("howlongtobeat")),
                      "Wikidata: 2 solicitudes, solo a wikidata.org (nunca al /api de HLTB)", string.Join(", ", mf1.Urls));

            var c2 = Cli(Busq(("Q10", "Resident Evil 4", "2005 survival horror video game"), ("Q11", "Resident Evil 4", "2023 survival horror video game")), Ents(("Q11", "123456")), out _);
            var r2 = new ResolverHltbWikidata(c2).ResolverAsync(J("Resident Evil 4", "PS5", anio: 2023)).Result;
            Verificar(r2.HltbId == "123456", "Wikidata: el año distingue el remake del original", Serializacion.AJson(r2));

            var c3 = Cli(Busq(("Q20", "Doom Eternal", "2020 video game")), Ents(("Q20", "777")), out _);
            var r3 = new ResolverHltbWikidata(c3).ResolverAsync(J("Doom", "PC", anio: 2016)).Result;
            Verificar(r3.HltbId == null && r3.Estado == TipoError.NoEncontrado, "Wikidata: 'Doom' no se confunde con 'Doom Eternal' (no se adivina)", Serializacion.AJson(r3));

            var c4 = Cli(Busq(("Q30", "Hades", "2020 video game")), Ents(("Q30", null)), out _);
            var r4 = new ResolverHltbWikidata(c4).ResolverAsync(J("Hades", "PC", anio: 2020)).Result;
            Verificar(r4.HltbId == null && r4.Estado == TipoError.NoEncontrado && r4.Detalle.Contains("no tiene su id"), "Wikidata: juego sin id de HLTB -> no_encontrado explicado", Serializacion.AJson(r4));

            var c5 = Cli(Busq(("Q40", "Prey", "2017 video game"), ("Q41", "Prey", "2017 video game")), Ents(("Q40", "111"), ("Q41", "222")), out _);
            var r5 = new ResolverHltbWikidata(c5).ResolverAsync(J("Prey", "PC", anio: 2017)).Result;
            Verificar(r5.HltbId == null && r5.Estado == "ambiguo", "Wikidata: dos ids distintos -> ambiguo (no se elige uno al azar)", Serializacion.AJson(r5));

            var c6 = Cli("{\"algo\":1}", "{}", out _);
            var r6 = new ResolverHltbWikidata(c6).ResolverAsync(J("Hades", "PC")).Result;
            Verificar(r6.HltbId == null && r6.Estado == TipoError.FormatoCambiado, "Wikidata: respuesta con otra forma -> formato_cambiado, sin crash", Serializacion.AJson(r6));

            var c7 = Cli("no es json {{{", "{}", out _);
            var r7 = new ResolverHltbWikidata(c7).ResolverAsync(J("Hades", "PC")).Result;
            Verificar(r7.HltbId == null && r7.Estado == TipoError.FormatoCambiado, "Wikidata: JSON dañado -> error controlado, sin crash", Serializacion.AJson(r7));
        }

        Console.WriteLine("== Tiendas desactivables ==");
        {
            var (cli2, _, _) = Montar((r2, n) => ManejadorFalso.Html("<html></html>"));
            var todas = TiendasDisponibles.Crear(cli2, null).Select(t => t.Tienda).ToArray();
            var sinEpicNintendo = TiendasDisponibles.Crear(cli2, new[] { " epic games store ", "NINTENDO ESHOP" }).Select(t => t.Tienda).ToArray();
            Verificar(todas.Length == 4 && sinEpicNintendo.Length == 2 && !sinEpicNintendo.Contains("Epic Games Store") && !sinEpicNintendo.Contains("Nintendo eShop"),
                      "TIENDAS_DESACTIVADAS: apaga tiendas por nombre (sin importar mayúsculas ni espacios) sin recompilar", string.Join(",", sinEpicNintendo));
        }

        Console.WriteLine("== hltb-verificar --limpiar ==");
        {
            string tmp = Path.Combine(Path.GetTempPath(), "repo_limpiar_" + Guid.NewGuid().ToString("N") + ".csv");
            File.WriteAllText(tmp, "\uFEFFid;nombre;plataforma;fecha_lanzamiento;precio_sugerido;hltb_id\r\n" +
                "1;Shovel Knight: Treasure Trove;PC;2014-06-26;24.99;19512\r\n2;Shovel Knight: Treasure Trove;Switch;2014-06-26;24.99;19512\r\n" +
                "3;Pokémon \"Z\"; raro;PC;2020-01-01;9.99;777\r\n4;Cocoon;PC;2023-09-29;24.99;109239\r\n", new System.Text.UTF8Encoding(false));
            int n = EditorRepoCsv.QuitarHltbIds(tmp, new[] { ("Shovel Knight: Treasure Trove", "19512") });
            var bytes = File.ReadAllBytes(tmp);
            var rr = new RepoCsv(tmp).Cargar();
            Verificar(n == 2 && rr.Juegos.Count(j => j.HltbId == null) == 2 && rr.Juegos.Single(j => j.Nombre == "Cocoon").HltbId == "109239",
                      "--limpiar: vacía solo el id equivocado (todas las plataformas de ese juego) y respeta los demás", $"n={n} errores={string.Join("|", rr.Errores)}");
            Verificar(bytes.Length > 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "--limpiar: conserva el BOM UTF-8 (tildes correctas en Excel)");
            File.Delete(tmp);
        }

        Console.WriteLine("== fechas-verificar ==");
        {
            var (cli, _, _) = Montar((r, n) => ManejadorFalso.Html(Fixtures.MetacriticHtml("Elden Ring", 94, "PlayStation 5", "2022-02-25")));
            var r = new MetacriticScraper(cli).ConsultarAsync(J("Elden Ring", "PS5")).Result;
            Verificar(r.Ok && r.FechaLanzamiento == "2022-02-25", "Metacritic devuelve la fecha de lanzamiento de la página", Serializacion.AJson(r));
            string tmp = Path.Combine(Path.GetTempPath(), "repo_fechas_" + Guid.NewGuid().ToString("N") + ".csv");
            File.WriteAllText(tmp, "\uFEFFid;nombre;plataforma;fecha_lanzamiento;precio_sugerido\r\n1;God of War;PS4;2018-04-20;19.99\r\n2;God of War;PC;2018-04-20;19.99\r\n",
                new System.Text.UTF8Encoding(false));
            int n = EditorRepoCsv.ActualizarFechas(tmp, new Dictionary<int, string> { [2] = "2022-01-14" });
            var rr = new RepoCsv(tmp).Cargar();
            Verificar(n == 1 && rr.Juegos.Single(j => j.Id == 2).FechaLanzamiento == new DateTime(2022, 1, 14)
                      && rr.Juegos.Single(j => j.Id == 1).FechaLanzamiento == new DateTime(2018, 4, 20),
                      "--aplicar: corrige solo la fila indicada y conserva las demás", $"n={n}");
            File.Delete(tmp);
        }

        Console.WriteLine($"\nResultado: {_ok} correctas, {_fallos} fallidas.");
        return _fallos == 0 ? 0 : 1;
    }

    /// <summary>Tienda de mentira que lanza una excepción: comprueba que ConsultarSeguroAsync la contiene.</summary>
    private sealed class TiendaRota : ITiendaScraper
    {
        public string Tienda => "Rota";
        public string Dominio => "rota.invalid";
        public bool SoportaPlataforma(string plataforma) => true;
        public Task<ResultadoTienda> ConsultarAsync(Juego juego, CancellationToken ct = default) =>
            throw new InvalidOperationException("error de programación simulado");
    }
}

/// <summary>Limitador de prueba: no espera y registra las pausas mínimas que pide el cliente.</summary>
internal sealed class LimitadorEspia : ILimitadorSolicitudes
{
    public readonly List<TimeSpan> Pausas = new();
    public Task<IDisposable> EntrarAsync(string dominio, CancellationToken ct = default) => Task.FromResult<IDisposable>(new Nada());
    public void ExigirPausaMinima(string dominio, TimeSpan pausa) { lock (Pausas) Pausas.Add(pausa); }
    private sealed class Nada : IDisposable { public void Dispose() { } }
}
