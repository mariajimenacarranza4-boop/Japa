# Conteo del REPO (Persona B) — verificación para la defensa

Fuente: `Data/juegos.csv` (separador `;`). Conteo hecho el 13-nov-2026.

| Dato | Valor |
|---|---|
| Filas (juego + plataforma) | 659 |
| **Juegos únicos (por nombre normalizado)** | **249** |
| Pares nombre + plataforma repetidos | 0 |
| IDs repetidos | 0 |
| Máximo de plataformas de un mismo juego | 6 |

## Filas por plataforma
| Plataforma | Filas |
|---|---|
| PC | 194 |
| Switch | 121 |
| PS5 | 95 |
| PS4 | 91 |
| Xbox One | 81 |
| Xbox Series | 77 |

## Cómo se cuenta (respuesta a "son menos de 200 juegos")
Cada fila es un juego en UNA plataforma (el Metacritic se pide por consola). Por eso hay 659 filas, pero los
juegos distintos son 249, y ambos números superan el mínimo de 200 del enunciado. No hay duplicados reales.

## Campos del REPO vacíos en esta versión
| Campo | Filas vacías de 659 | Nota |
|---|---|---|
| fecha_lanzamiento | 0 | Falta verificar con `fechas-verificar` |
| precio_sugerido | 0 | Es un precio de referencia general |
| imagen_url | 659 | La imagen sale de la tienda |
| steam_appid | 658 | Se busca por nombre |
| nombre_busqueda | 659 | Se usa el nombre normalizado |
| metacritic_slug | 659 | Se resuelve por búsqueda |
| hltb_id | 658 | PENDIENTE: correr `hltb-autocompletar --aplicar` y `hltb-verificar --limpiar` |

## Comando para repetir el conteo (Python 3)
    python3 -c "import csv;r=list(csv.DictReader(open('Data/juegos.csv',encoding='utf-8-sig'),delimiter=';'));print(len(r),len({' '.join(x['nombre'].lower().split()) for x in r}))"

**Cambio del 14-nov-2026:** se quitó *Guilty Gear -Strive-* (4 filas) y se agregó *PEAK* (PC, PS5 y Xbox Series; no existe en PS4). Los precios sugeridos de PEAK
(PC 7.99, consolas 6.99) son de referencia y deben confirmarse en las tiendas.
