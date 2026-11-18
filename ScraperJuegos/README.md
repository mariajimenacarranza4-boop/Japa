# ScraperJuegos (Persona B: REPO + scrapers)

Requisito: .NET 8 SDK.

```
dotnet run                                  # valida el REPO (juegos distintos, errores, cobertura de tiendas)
dotnet run -- autotest                      # pruebas sin internet (REPO, nombres, oferta, las 4 tiendas, Epic vía CheapShark, errores, robots...)
dotnet run -- probar 10                     # las 4 tiendas + Metacritic + HLTB contra los sitios REALES sobre 10 juegos
dotnet run -- probar 3 --juego "Elden Ring" # un juego concreto (también: --plataforma PS5)
dotnet run -- probar --juego "Juego Nuevo" --precio 49.99   # juego que NO está en el REPO: se consulta igual (sin tocar el CSV)
dotnet run -- hltb-autocompletar --aplicar  # descubre SOLO los hltb_id faltantes (vía Wikidata) y los copia a Data/juegos.csv
dotnet run -- hltb-pendientes               # juegos del REPO sin hltb_id (salida/hltb_pendientes.csv)
dotnet run -- hltb-importar                # copia los ids que llenó en salida/hltb_pendientes.csv hacia Data/juegos.csv
dotnet run -- hltb-verificar [--limpiar]               # comprueba cada hltb_id contra https://howlongtobeat.com/game/ID
dotnet run -- probar --juego "Elden Ring" --hltb-id 68151   # HLTB para un juego usando su id
dotnet run -- fechas-verificar [N] [--aplicar]  # compara fecha_lanzamiento del REPO con Metacritic por plataforma (salida/fechas_verificar.csv)
dotnet run -- diagnostico                    # por qué falla cada fuente: robots.txt de cada sitio, respuestas crudas y prueba rápida (salida/diagnostico/informe.txt)
dotnet run -- capturar "https://..." pagina.html   # guarda una página cruda (respeta robots.txt) en salida/diagnostico/
dotnet run -- catalogos                     # salida/valores_validos.json (filtros de C)
dotnet run -- exportar                      # salida/repo_juegos.json (para cargar a la base en la nube)
```

Opciones: `--repo RUTA.csv`, `--salida carpeta`, `--plataforma`, `--precio`, `--anio` (estas tres para juegos fuera del REPO).
Tiendas apagables con la variable `TIENDAS_DESACTIVADAS` (ej. `$env:TIENDAS_DESACTIVADAS="Eneba"`): una tienda caída o bloqueada, apagada, no gasta solicitudes ni ensucia las mediciones.
Tiendas: Steam, Epic Games Store, Nintendo eShop y Eneba (lista única en `TiendasDisponibles.cs`).
Epic Games Store se consulta **vía la API pública de CheapShark** (store.epicgames.com responde 403): se busca su id por nombre en `/api/1.0/stores` (o se fija con `CHEAPSHARK_EPIC_STORE_ID`), respeta robots.txt, pide 1 solicitud/s y, si CheapShark bloquea, deja de insistir unos minutos. **Respaldo:** si CheapShark falla y existe `ITAD_API_KEY` (clave gratuita de isthereanydeal.com/apps), Epic se consulta con la API oficial de IsThereAnyDeal.
Si Nintendo rota su clave pública de búsqueda: variable `NINTENDO_ALGOLIA_KEY` (sin recompilar). Defina `SCRAPER_CONTACTO` (su correo) para un User-Agent completo.
Salidas: `salida/prueba_scrapers.json` (formato del Contrato 1) y `salida/fallos.log`.

## Para A (consumir los scrapers)
```csharp
var limitador = new LimitadorDominio(maxConcurrenciaPorDominio: 2, pausaMinima: TimeSpan.FromSeconds(1)); // o el suyo: ILimitadorSolicitudes
var http = ClienteHttp.Crear(new ConfigScraping(), limitador, new LogFallosArchivo("salida/fallos.log"));
ITiendaScraper[] tiendas = TiendasDisponibles.Crear(http);   // las 4 tiendas
var meta = new MetacriticScraper(http); var hltb = new HltbScraper(http);
ResultadoTienda r = await TiendasDisponibles.ConsultarSeguroAsync(tiendas[0], juego);   // nunca lanza; r.Ok / r.Error
var c = CalculadorOferta.Consolidar(resultadosTiendas, juego.PrecioSugerido); // mejor precio, descuento, en_oferta
var juegos = new RepoCsv(ruta).Cargar().AgruparPorJuego();    // un registro por juego (Nivel 1)
```
Documentación: `docs/Documentacion_B.md`. No suba claves al repositorio (`.gitignore` ya excluye `.env` y `salida/`).

HowLongToBeat: su robots.txt prohíbe /api, por eso solo se lee /game/ID (el hltb_id va en Data/juegos.csv; `hltb-autocompletar` lo descubre vía Wikidata y `hltb-verificar` lo confirma). Citar a HLTB como fuente en el sitio.
