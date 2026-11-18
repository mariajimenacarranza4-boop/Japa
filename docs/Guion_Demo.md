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
| 6 | C | Un juego con N/D (ej. Sea of Stars sin HLTB) y la sección *Estado de las fuentes* del detalle | "Si una fuente falla, se conserva el último dato bueno con su fecha; lo que no hay se muestra N/D. Las tiendas solo venden digital, por eso los filtros solo ofrecen Digital (limitación documentada)." | Datos faltantes / consistencia |
| 6b | B | `dotnet run -- diagnostico` (sección robots.txt) | "Antes de pedir una página leemos su robots.txt. Epic responde 403 y CheapShark prohíbe su API en robots.txt, así que Epic quedó desactivada: no lo evadimos." | Fuentes y ética |
| 7 | B | Calcular a mano la oferta de un juego | Fórmula: (regular − actual) / regular × 100; coincide con lo que muestra la tarjeta. | Indicador de oferta |
| 8 | B | `dotnet run` (conteo ≥ 200) | "Juegos distintos, no filas." | REPO 200+ |
| 9 | A | Corrida con logs de los 3 niveles | Juegos, fuentes y tiendas ejecutándose a la vez; semáforo por dominio. | Paralelismo |
| 10 | A | Tabla y gráficos de speedup/eficiencia | 1, 2, 4 y 12 trabajadores. Con 249 juegos el speedup es 1,19 porque Eneba va de 1 en 1 con 3 s (cola de ≈2 000 s); con 40 juegos fue 2,18. Amdahl: f ≈ 0,87 → tope ≈ 1,2. | Rendimiento |
| 11 | C | Pestaña *Actions* → **Actualizar catálogo** | Corridas con evento `schedule` (automáticas) cada 12 h; justificar el intervalo; commits de `github-actions[bot]`. | Actualización recurrente |
| 12 | B+C | **En vivo:** editar `juegos.csv` en GitHub, agregar un juego → *Run workflow* | Una corrida real tarda ≈1 h (Eneba va lento a propósito), así que se muestra que arrancó y se enseña una corrida anterior que agregó juegos (ej. PEAK). | REPO dinámico |

**Plan B si algo falla en vivo:** capturas de pantalla de cada paso en `docs/capturas/` y una corrida exitosa previa
enlazada desde el pie del sitio.

**Preguntas que cualquiera debe poder responder:** diagrama de los 3 niveles · cómo se calcula el speedup · cómo se
define una oferta · cómo se actualiza el sitio (cron de GitHub Actions → scraping → validación → commit → GitHub Pages)
· por qué hilos y no procesos · dónde están las claves (secretos de GitHub, no en el código).
