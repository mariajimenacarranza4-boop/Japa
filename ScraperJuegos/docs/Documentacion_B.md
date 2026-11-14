# Documentación de la Persona B — REPO y web scraping

Proyecto III, IC3101 Arquitectura de Computadoras. Este archivo contiene las secciones que le tocan a B:
**Sección 2** (REPO), **Sección 3** (estrategia por sitio e indicador de oferta) y la parte de
**errores de la Sección 4**. C las consolida en el documento técnico final.

Lenguaje: **C# (.NET 8)**, sin paquetes externos (solo la biblioteca estándar).

---

## Sección 2. Estructura del REPO y cómo se actualiza

### 2.1 Dónde vive y qué contiene
El REPO es el archivo `Data/juegos.csv` (UTF-8, separado por `;`). Una **fila = un juego en una plataforma**
(Elden Ring en PS5 y en PC son dos filas, porque Metacritic y las tiendas dan datos distintos por plataforma).

| Columna | Obligatoria | Uso |
|---|---|---|
| `id` | sí | Identificador único de la fila |
| `nombre` | sí | Nombre del juego |
| `plataforma` | sí | `PS5`, `PS4`, `Xbox Series`, `Xbox One`, `PC`, `Switch` |
| `fecha_lanzamiento` | sí | `aaaa-mm-dd` |
| `precio_sugerido` | sí | USD, precio de referencia |
| `imagen_url` | no | Imagen de respaldo si ninguna tienda la entrega |
| `steam_appid` | no | Evita la búsqueda por nombre en Steam |
| `nombre_busqueda` | no | Nombre exacto en las fuentes cuando difiere (también cuenta como nombre aceptado) |
| `metacritic_slug` | no | Parte de la URL de Metacritic (`elden-ring`) |
| `hltb_id` | no | Id numérico de HowLongToBeat (ruta más estable que la búsqueda) |

**Tamaño:** 249 juegos distintos (660 filas por plataforma). El enunciado pide 200 juegos: se cuentan juegos
*distintos*, no filas, y por eso el programa lo reporta así (`dotnet run`). El archivo original tenía 136 juegos
distintos (347 filas), que habría fallado el requisito.

### 2.2 Cómo se modifica sin tocar el código
Se edita `Data/juegos.csv` (Excel o cualquier editor) y se vuelve a correr. El REPO **se lee completo en cada ejecución**;
no hay lista en el código. Detalles de robustez de la lectura:
- Las columnas se reconocen por el **nombre del encabezado**, no por posición.
- Una fila mala (id repetido, fecha inválida, plataforma desconocida, precio ≤ 0, juego+plataforma repetido)
  se **reporta con su número de línea** y **no detiene** la carga de las demás.
- Acepta `59,99` y `10/11/2026` por si Excel cambia el formato.

**Demostración para la defensa:** agregar una línea al CSV, correr `dotnet run` (sube el conteo) y
`dotnet run -- probar 1 --juego "Nombre nuevo"` (el juego nuevo se consulta).

### 2.3 Cambio de base de datos
A solo conoce la interfaz `IRepoJuegos` (`Cargar()`), no el CSV. Para pasar a una base en la nube basta otra
clase que implemente esa interfaz. `dotnet run -- exportar` genera `salida/repo_juegos.json` para importar el REPO
a Firebase/Supabase (colección `repo_juegos`).

---

## Sección 3. Estrategia de scraping por sitio e indicador de oferta

### 3.1 Fuentes
| Fuente | Qué da | Cómo se consulta | Plataformas |
|---|---|---|---|
| **Steam** (tienda 1) | precio actual, regular, enlace, imagen | JSON de la propia tienda: `/api/storesearch` (1 solicitud/juego); `/api/appdetails` si el REPO trae `steam_appid`. USD (`cc=us`) | PC |
| **Epic Games Store** (tienda 2, **vía CheapShark**) | precio actual, regular, enlace, imagen | La tienda directa responde 403 y no se evade. Se usa la API pública y gratuita de CheapShark, un comparador de precios que rastrea Epic: `GET www.cheapshark.com/api/1.0/deals?storeID=25&title=...&exact=1` (y `exact=0` solo si no hay coincidencia exacta). Precios en USD como texto; enlace por el redireccionador de CheapShark a la página del juego en Epic. **El dato llega por intermediario y puede tener horas de atraso.** | PC |
| **Nintendo eShop** (tienda 3) | precio actual, regular, enlace, imagen | índice de búsqueda de nintendo.com (Algolia, clave pública de solo búsqueda, configurable por `NINTENDO_ALGOLIA_KEY`); campos `msrp` / `salePrice` | Switch |
| **Eneba** (tienda 4) | precio de la clave más barata (sin precio regular), enlace, imagen | página de búsqueda; solo datos estructurados schema.org (JSON-LD). Solo claves GLOBAL, solo USD, plataforma deducida del nombre | PC, PS5, PS4, Xbox, Switch |
| **Metacritic** | Metascore **de la consola del REPO** | `/game/{slug}/?platform={plataforma}`; JSON-LD y, de respaldo, HTML | todas |
| **HowLongToBeat** | horas (completo = *Completionist*; también principal y extra) | solo la página pública del juego `https://howlongtobeat.com/game/{hltb_id}` (el `hltb_id` es dato del REPO). **No** usa el buscador `/api` | todas |

`formato` es `digital` en todas las tiendas. **Limitación conocida:** ninguna tienda implementada vende físico, así que el
filtro "físico" quedará vacío salvo que el equipo agregue una fuente física (por ejemplo Amazon, que suele bloquear).
Xbox solo tiene precio vía Eneba (claves); Switch vía Nintendo eShop y Eneba. Los conteos de cobertura los calcula `dotnet run` a partir de la lista de tiendas.
Agregar una tienda = una clase que implemente `ITiendaScraper` + una línea en `TiendasDisponibles.cs` + su nombre en `Catalogos.Tiendas` (una prueba avisa si se olvida).

### 3.2 Coincidencia de nombres y plataforma
El mismo juego se llama distinto en cada sitio (`EA SPORTS FC™ 24`, `ELDEN RING Deluxe Edition`). Estrategia:
1. **Normalizar:** sin tildes, minúsculas, sin ™ ® ©, `&` → `and`, sin puntuación, "the" inicial fuera, romanos → dígitos
   (VII = 7) y sin sufijos de edición (*Standard/Deluxe/Ultimate/Complete Edition, GOTY, Director's Cut, Remastered…*).
2. **Coincidencia = nombres normalizados iguales.** Por eso `Doom` ≠ `Doom Eternal`, `God of War` ≠ `God of War Ragnarök`,
   `FIFA 23` ≠ `FIFA 22`.
3. **Verificar plataforma** en cada fuente (Steam: Windows; Nintendo: Switch; Eneba: la plataforma deducida del título; Metacritic: `gamePlatform`).
   En Epic (vía CheapShark) se descartan los títulos que no coinciden con el juego (DLC, bandas sonoras, ediciones con otro nombre) y, entre ediciones del mismo juego, se toma la más barata (la base).
4. **Si hay duda se devuelve `no_encontrado`; nunca se adivina.** En HLTB, ante varios juegos con el mismo nombre
   (remakes) el `hltb_id` del REPO identifica el juego exacto, y el scraper verifica que el nombre de la página coincida.
5. Si un nombre legítimo no coincide (ej. PC: *Marvel's Spider-Man* vs *Marvel's Spider-Man Remastered*), se corrige en el
   REPO con `nombre_busqueda`, que es el nombre exacto que se acepta.

### 3.3 Indicador de oferta (fórmula del equipo)
1. **Principal (por tienda):** en oferta si `precio_actual < precio_regular` de esa tienda.
   `descuento % = (precio_regular − precio_actual) / precio_regular × 100`
2. **Apoyo:** si la tienda no informa `precio_regular`, se compara contra el `precio_sugerido` del REPO con la misma fórmula.
3. Si no se cumple ninguna: no está en oferta, descuento 0.

Ejemplo: regular 59.99, actual 29.99 → (59.99 − 29.99) / 59.99 × 100 = **50.01 %**, en oferta.
Tolerancia de 1 centavo (evita falsos positivos por redondeo). Todo en USD para ser comparable con el precio sugerido.
**Mejor precio:** el **menor** `precio_actual` entre las tiendas que respondieron (empate: mayor descuento). El precio tachado
y el porcentaje que se muestran son los **de esa misma tienda**, así `en_oferta`, `descuento_pct` y la tarjeta son coherentes.
Implementado en `Oferta/CalculadorOferta.cs` (`Evaluar` y `Consolidar`) para que A lo llame sin reimplementarlo.

### 3.3b Moneda
Todo se compara en **USD**. Si una tienda responde en otra moneda, su scraper devuelve `formato_cambiado` y, como segunda barrera,
`CalculadorOferta.Consolidar` ignora cualquier resultado con `moneda` distinta de USD, para que nunca gane un "mejor precio" falso.
Eneba no tiene precio regular: se omite `precio_regular` y la oferta se evalúa contra el `precio_sugerido` del REPO.

### 3.3c Juegos fuera del REPO y cambios
- `dotnet run -- probar --juego "Nombre"`: si no está en el REPO se consulta igual (PC, PS5 y Switch, o la de `--plataforma`); con `--precio` se evalúa la oferta y con `--anio` HowLongToBeat desempata. Sin resultados -> `no_encontrado`, nunca una caída.
- Un scraper con un error de programación no detiene la prueba (`TiendasDisponibles.ConsultarSeguroAsync`); `Program` captura cualquier excepción y sale con un mensaje.
- El CSV se puede leer aunque esté abierto en Excel; un archivo ilegible o inexistente se reporta como error del REPO.

### 3.4 Imagen
Steam entrega `header_image` (o `…/steam/apps/{id}/header.jpg`); Epic, Nintendo y Eneba entregan la imagen de su ficha.
Si ninguna tienda responde, C usa `imagen_url` del REPO.

### 3.5 Términos de uso y cortesía — qué se revisó
El cliente HTTP **lee el `robots.txt` de cada dominio antes de pedir cualquier URL** (RFC 9309: grupo propio o `*`, regla más
específica gana, respeta `Crawl-delay`). Si no lo puede leer, **no pide nada**. Cada bloqueo queda en el log.
Además: User-Agent honesto (`ScraperJuegosTEC/1.0 (proyecto academico IC3101 TEC San Carlos; contacto: …)`), pausa mínima de
1 s entre solicitudes al mismo dominio y máximo 2 simultáneas por dominio.

| Sitio | Estado de la revisión | Pendiente |
|---|---|---|
| store.steampowered.com | Fuentes secundarias indican que su `robots.txt` es abierto, y que `api.steampowered.com` sí prohíbe acceso automatizado (por eso **no** se usa). | Verificar el `robots.txt` y los Términos de Steam y anotar fecha |
| store.playstation.com | **PlayStation Store se eliminó del proyecto (decisión del equipo).** Evidencia (11-nov-2026, `dotnet run -- diagnostico`): `robots.txt` permite `/en-us/search/` y la página responde HTTP 200, pero el HTML ya no trae resultados (solo casillas de carga); los productos los pide el navegador a una API interna sin documentación pública, que no se considera apropiado leer sin autorización. | Mencionar en el informe como limitación: PS4/PS5 conservan Metacritic, HowLongToBeat y los precios de Eneba (claves) |
| store.epicgames.com | **Observado el 11-nov-2026 con `dotnet run -- diagnostico`:** la tienda responde HTTP 403 (acceso denegado) a las consultas automáticas. El scraper lo clasifica como `bloqueo`, no reintenta y no intenta evadirlo (respeto a los términos y a las medidas anti-bots del sitio). **Decisión:** no se evade; los precios de Epic se obtienen de la API pública de CheapShark (`EpicCheapSharkScraper`, ver 3.1). El acceso directo (`EpicGamesScraper`) queda en el código como evidencia, sin registrar. | Anotar la fecha de la revisión de `robots.txt` y términos con el `informe.txt` |
| nintendo.com / algolia.net | No verificado. La clave de búsqueda es pública pero puede rotar (403 -> `bloqueo`); el índice puede cambiar de nombre (`NINTENDO_ALGOLIA_INDEX`) | Revisar términos de Nintendo y `robots.txt` de ambos dominios |
| eneba.com | No verificado. Es el sitio más propenso a anti-bots; `bloqueo` / `formato_cambiado` son resultados esperables. La vía oficial es su API de afiliados (requiere registro) | Revisar `robots.txt`/términos; si bloquea, documentarlo como limitación |
| metacritic.com | No verificado. Es la fuente más propensa a bloqueo y sus términos son restrictivos | Revisar; si bloquea, documentar el cambio |
| howlongtobeat.com | **Verificado con `dotnet run -- diagnostico`** (anotar fecha de la corrida): su `robots.txt` prohíbe `/api` (`Disallow: /api`), que es el buscador interno; en cambio `/game/ID` está PERMITIDO (ninguna regla aplica a esa ruta). Por eso el scraper usa solo `/game/ID` y el `hltb_id` se guarda en el REPO. | Anotar la fecha y adjuntar `salida/diagnostico/informe.txt`; mencionar a HowLongToBeat como fuente en el sitio |

| wikidata.org (solo `hltb-autocompletar`) | Se usa su **API pública** (`/w/api.php`) con User-Agent descriptivo y 1 solicitud/s, como pide la etiqueta de la API de Wikimedia. En esta única herramienta no se consulta `robots.txt` (regula a rastreadores de páginas, no a la API); el resto del proyecto sí lo respeta siempre. No corre durante el scraping normal. | Anotar fecha de la primera corrida; definir `SCRAPER_CONTACTO` |

**Apagar tiendas caídas sin recompilar:** variable de entorno `TIENDAS_DESACTIVADAS` (ej. `Epic Games Store,Eneba`).
Una tienda apagada no consume solicitudes ni ensucia el log ni las mediciones de rendimiento. Es una decisión de configuración, no de código.

> Estas revisiones **no se pudieron hacer en vivo** al desarrollar (sin acceso a esos sitios). Antes de la entrega:
> abrir cada `robots.txt` y los términos, y anotar aquí fecha y conclusión como evidencia para la defensa.


### 3.5b Registro de revisión de robots.txt y términos (completar antes de entregar)
Una fila por sitio. Fecha = día en que se abrió el `robots.txt` y la página de términos. Conclusión = qué se permite y qué ruta usa el scraper.

| Sitio | URL de robots.txt | URL de términos | Fecha revisada | Conclusión / ruta usada |
|---|---|---|---|---|
| Steam | store.steampowered.com/robots.txt | steampowered.com/subscriber_agreement | ____ | ____ |
| Nintendo | nintendo.com/robots.txt y algolia.net | nintendo.com/en-US/Legal/ | ____ | ____ |
| Eneba | eneba.com/robots.txt | eneba.com (términos) | ____ | ____ |
| Metacritic | metacritic.com/robots.txt | fandom.com/terms-of-use | ____ | ____ |
| HowLongToBeat | howlongtobeat.com/robots.txt | howlongtobeat.com (términos) | ____ | `/api` prohibido; solo `/game/ID` |
| Wikidata | wikidata.org/robots.txt | API etiquette de Wikimedia | ____ | API pública, 1 solicitud/s |
| CheapShark (Epic) | cheapshark.com/robots.txt | apidocs.cheapshark.com | 14-nov-2026 | **`Disallow: /api/1.0/`**: el robots.txt prohíbe la API, así que el scraper no la consulta (bloqueo controlado). Epic queda apagada (`TIENDAS_DESACTIVADAS`) |
| Epic (directo) | store.epicgames.com/robots.txt | epicgames.com/site/es-ES/tos | 11-nov-2026 | Responde 403 a consultas automáticas; no se evade (se usa CheapShark) |

Adjuntar `salida/diagnostico/informe.txt` de la corrida de `diagnostico` como evidencia.

### 3.6 Limitaciones conocidas (para la defensa)
- **Formato físico:** las tiendas implementadas solo venden digital; el filtro "físico" queda vacío. Amazon y similares bloquean el scraping y el reparto indica no pelear con bloqueos. Decisión del equipo: ____.
- **Cobertura de tiendas:** PC tiene Steam y Eneba (Epic Games Store queda apagada: la tienda directa responde 403 y el robots.txt de CheapShark prohíbe su API; ver 3.5b); Switch tiene Nintendo eShop y Eneba; PS4, PS5 y Xbox solo tienen Eneba (PlayStation Store se eliminó). **En las pruebas reales, Eneba no devolvió claves de PlayStation para ningún juego (solo PC y Xbox)**: las versiones de PS4/PS5 quedan sin precio; los juegos multiplataforma tienen precio por sus otras plataformas y solo 6 exclusivos de PlayStation (Bloodborne, Gran Turismo 7, Astro Bot, Demon's Souls, Death Stranding 2, Ghost of Yōtei) quedan sin ninguna tienda. Se evaluó la API de PlatPrices (requiere clave) y el equipo decidió no depender de un servicio con clave. Con el REPO actual, 184 de 249 juegos tenían precio en 2 tiendas distintas antes de agregar Epic (volver a contar con `dotnet run`). Si Eneba bloquea, PS y Xbox quedan sin precio.
- **REPO local:** vive en `Data/juegos.csv` (cumple "dinámico"); `exportar` genera el JSON para cargarlo a la nube si el equipo lo decide.

### HowLongToBeat: por qué `/game/ID` y de dónde salen las horas y fechas
- **Regla del sitio:** el `robots.txt` de HLTB prohíbe `/api` (el buscador interno). El proyecto respeta robots.txt, así que **no** usa
  `/api`; solo lee la página pública `https://howlongtobeat.com/game/ID`, que sí está permitida. HLTB se cita como fuente en el sitio.
- **El `hltb_id`** (el número de la URL del juego en HLTB) es un dato del REPO, como `steam_appid`: se escribe una vez por juego en
  `Data/juegos.csv` (las demás plataformas del mismo juego lo heredan). Sin `hltb_id` el scraper devuelve `no_encontrado` con una
  instrucción clara; nunca adivina ni toca `/api`. Se verifica que el nombre de la página coincida con el del REPO (detecta ids mal copiados).
- **Los tiempos NO están guardados ni "quemados"**: se leen en vivo de la página en cada consulta. Cada resultado trae `fecha_consulta`
  con la hora real (`DateTimeOffset.UtcNow`) del momento de la consulta; no hay fechas fijas en el código (solo `fecha_lanzamiento`,
  que es un dato del REPO). Una prueba automática lo comprueba.
- **Cómo se obtienen los ids sin llenarlos a mano (`dotnet run -- hltb-autocompletar --aplicar`):** como HLTB no permite su buscador,
  el id se descubre en **Wikidata**, que guarda para muchos videojuegos la propiedad «HowLongToBeat ID» (P2816; configurable con
  `HLTB_WIKIDATA_PROP`) y ofrece una API pública. Estrategia sin adivinar: se busca por nombre; el nombre debe coincidir tras normalizar
  (mismo criterio de §3.2); si la descripción trae un año y difiere más de 1 año del REPO se descarta (distingue un remake del original);
  si queda **un** id -> se usa; si no hay ninguno -> `no_encontrado`; si hay varios distintos -> `ambiguo` (se deja para llenar a mano).
  Es una herramienta de preparación del REPO (se corre una vez, no en cada scraping): 2 solicitudes por juego, 1 por segundo.
  Luego `hltb-verificar` confirma contra la página real `/game/ID` que cada id es ese juego y trae tiempos; los ids malos se detectan ahí.
  Los juegos que Wikidata no resuelva quedan en `salida/hltb_autocompletar.csv` con un enlace de búsqueda para completarlos manualmente.
- Comandos: `hltb-autocompletar` (descubre ids), `hltb-pendientes` (lista los que faltan), `hltb-importar` (copia ids al REPO), `hltb-verificar` (confirma cada id contra la página real).

---

## Sección 4 (parte de B). Manejo de errores

Todo scraper **devuelve** un resultado, nunca lanza una excepción hacia A (`Seguro.cs`). Los 4 tipos del Contrato 1:

| `error` | Cuándo |
|---|---|
| `timeout` | sin conexión, tiempo agotado o servidor caído (5xx) **después de agotar los reintentos** |
| `bloqueo` | 403/401, 429 persistente, página anti-bots/captcha, o `robots.txt` no lo permite / no se pudo leer |
| `formato_cambiado` | el dato esperado no apareció o el JSON/HTML tiene otra forma (incluye excepciones de lectura inesperadas) |
| `no_encontrado` | 404, sin coincidencia segura de nombre/plataforma, sin precio |

**Reintentos y espera:** timeout de 15 s por intento; hasta 3 reintentos con espera creciente 1 s, 2 s, 4 s (+ aleatorio); en
429 se respeta `Retry-After`. **403 y 404 no se reintentan** (insistir ante un bloqueo sería faltarle el respeto al sitio).
**Log** (`salida/fallos.log`, una línea por evento, seguro entre hilos): `fecha_utc;juego;plataforma;fuente;tipo;detalle`.
Los reintentos aparecen con tipo `reintento`. **Detección de cambio de formato:** si falta un campo clave, es
`formato_cambiado` con el detalle de qué faltó, no un crash.

**Para A (sin estado compartido):** los scrapers no usan variables globales; el `HttpClient` y la caché de `robots.txt`
son seguros entre hilos (la caché usa `Lazy<Task>` por dominio: 60 juegos en paralelo descargan el robots.txt una sola vez).
A inyecta su propio `ILimitadorSolicitudes`; si no, se usa `LimitadorDominio` (semáforo por dominio + pausa mínima).

## Verificación
`dotnet run -- autotest` ejecuta las pruebas sin internet (REPO, nombres, oferta, las 4 tiendas + Metacritic + HLTB con cada tipo de error,
robots.txt, reintentos, límite por dominio, 60 juegos en 16 hilos). **Las pruebas usan respuestas simuladas con la forma que
tienen los sitios; no prueban los sitios reales.** Contra los sitios reales: `dotnet run -- probar 10`.

## HowLongToBeat: fuente y campos (revisión)

- **Fuente:** HowLongToBeat (https://howlongtobeat.com). Debe citarse como fuente de los tiempos en el sitio web y en el informe.
- **Qué se revisó:** su `robots.txt` contiene `Disallow: /api` (el buscador interno) y no restringe `/game/ID`. Por eso el scraper **nunca** llama a `/api`; solo lee la página pública `https://howlongtobeat.com/game/ID`, con User-Agent honesto, pausa de 1 s por dominio, timeout y reintentos.
- **Campos que se recuperan de la página:** horas 100 % (completionist), historia principal, historia + extras, todos los estilos, nombre en HLTB, desarrollador, plataformas, año de lanzamiento, nota de usuarios, número de reseñas, cuántos lo completaron / lo juegan / lo tienen pendiente / lo abandonaron, e imagen. Lo que HLTB no publique se guarda como vacío y la web muestra "N/D".
- **Requisito:** cada juego necesita su `hltb_id` (el número de la URL) en `Data/juegos.csv`; si falta, se devuelve `no_encontrado` con un mensaje claro (no se adivina ni se usa `/api`). Flujo: `hltb-pendientes` -> llenar ids -> `hltb-importar` -> `hltb-verificar`.
- **Fecha y hora:** `fecha_consulta` se toma con `DateTimeOffset.UtcNow` en el momento de cada consulta; no hay fechas fijas en el código.
