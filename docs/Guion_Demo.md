# Guion de la demostración (≈10 minutos)

**Preparar antes de entrar:** celular con datos móviles (wifi apagado), pestañas abiertas: sitio público, pestaña
*Actions* del repo, `juegos.csv` en GitHub, terminal en `ScraperJuegos/`. Tener una corrida reciente en verde.

| # | Quién | Qué se muestra | Qué se dice | Requisito que cubre |
|---|---|---|---|---|
| 1 | C | URL pública abierta **en el celular con datos móviles** | "No es localhost: está en GitHub Pages y abre desde cualquier red." | Sitio público |
| 2 | C | Tarjetas | Imagen, título, consolas, mejor precio entre tiendas, precio regular tachado, % de descuento, Metacritic. | Tarjeta |
| 3 | C | Filtrar *Plataforma = Switch* + *Tienda = Nintendo eShop*, luego ordenar *Precio ascendente* | "Los filtros y el orden se combinan; el precio mostrado es el de la tienda filtrada." | Filtros + orden |
| 4 | C | Ordenar por Metacritic desc y asc | "Los N/D quedan siempre al final." | Orden |
| 5 | C | Abrir un juego | Precios ordenados con enlace (abrir uno y ver que es el producto correcto), sugerido, Metacritic, HLTB, última actualización. | Detalle |
| 6 | C | Un juego con N/D, una fuente "falló" y el filtro *Formato = Físico (sin datos)* | "Si una fuente falla, se conserva el último dato bueno con su fecha; lo que no hay se muestra N/D; Físico existe pero las tiendas solo venden digital (limitación documentada)." | Datos faltantes / consistencia |
| 6b | B | Detalle de un juego de PC con precio de Epic | "Epic bloquea con 403 y no lo evadimos: el precio viene de la API pública de CheapShark, que rastrea Epic." | Fuentes y ética |
| 7 | B | Calcular a mano la oferta de un juego | Fórmula: (regular − actual) / regular × 100; coincide con lo que muestra la tarjeta. | Indicador de oferta |
| 8 | B | `dotnet run` (conteo ≥ 200) | "Juegos distintos, no filas." | REPO 200+ |
| 9 | A | Corrida con logs de los 3 niveles | Juegos, fuentes y tiendas ejecutándose a la vez; semáforo por dominio. | Paralelismo |
| 10 | A | Tabla y gráficos de speedup/eficiencia | 1, 2, 4 y máximo; Amdahl y cuellos de botella. | Rendimiento |
| 11 | C | Pestaña *Actions* → **Actualizar catálogo** | Corridas con evento `schedule` (automáticas) cada 12 h; justificar el intervalo; commits de `github-actions[bot]`. | Actualización recurrente |
| 12 | B+C | **En vivo:** editar `juegos.csv` en GitHub, agregar un juego → *Run workflow* | Mientras corre (≈15 min en modo real) se sigue con las preguntas; al final se recarga el sitio y el juego aparece. | REPO dinámico |

**Plan B si algo falla en vivo:** capturas de pantalla de cada paso en `docs/capturas/` y una corrida exitosa previa
enlazada desde el pie del sitio.

**Preguntas que cualquiera debe poder responder:** diagrama de los 3 niveles · cómo se calcula el speedup · cómo se
define una oferta · cómo se actualiza el sitio (cron de GitHub Actions → scraping → validación → commit → GitHub Pages)
· por qué hilos y no procesos · dónde están las claves (secretos de GitHub, no en el código).
