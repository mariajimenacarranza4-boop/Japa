# Entrega de B a A — límites por sitio y reporte de errores

Qué recibe A: los scrapers (Contrato 1, ver `Contratos/Resultados.cs` e `Interfaces.cs`), los límites razonables por sitio
y el formato de error que los scrapers devuelven.

## 1. Los scrapers (Contrato 1)
- `ITiendaScraper.ConsultarAsync(Juego, ct)` -> `ResultadoTienda` (Steam, Epic, Nintendo eShop, Eneba)
- `IMetacriticScraper.ConsultarAsync(Juego, ct)` -> `ResultadoMetacritic`
- `IHltbScraper.ConsultarAsync(Juego, ct)` -> `ResultadoHltb`
- NUNCA lanzan excepciones (solo dejan pasar la cancelación que A pida con `ct`).
- SIN estado compartido: cada llamada es independiente, se pueden ejecutar en paralelo (niveles 1, 2 y 3).
- `ITiendaScraper.SoportaPlataforma(plataforma)` dice si la tienda aplica a esa consola.
- Para obtener la lista de tiendas: `TiendasDisponibles.Crear(http)`. Para llamar con red de seguridad: `TiendasDisponibles.ConsultarSeguroAsync`.

## 2. Límites razonables por sitio
Valores por defecto de `LimitadorDominio` (A puede inyectar el suyo con la misma interfaz `ILimitadorSolicitudes`):

| Dominio | Máx. simultáneas | Pausa mínima entre solicitudes |
|---|---|---|
| store.steampowered.com | 2 | 1 s |
| www.cheapshark.com (Epic Games Store vía su API pública; ver 3.1) | 2 | 1 s |
| api.isthereanydeal.com (respaldo de Epic con clave; solo si ITAD_API_KEY existe) | 2 | 1 s |
| www.nintendo.com | 2 | 1 s |
| www.eneba.com | 2 | 1 s |
| metacritic.com | 2 | 1 s |
| howlongtobeat.com (solo `/game/ID`) | 2 | 1 s |

- Si el `robots.txt` de un sitio trae `Crawl-delay`, el cliente sube la pausa de ese dominio (`ExigirPausaMinima`); nunca la baja.
- Importante para las mediciones: subir el número de trabajadores NO sube la presión sobre un sitio, porque el límite es por dominio.
  Ese tope es parte natural del análisis de cuellos de botella (límites de los sitios) y de la Ley de Amdahl.

## 3. Robustez que ya trae cada solicitud (`ConfigScraping`)
| Parámetro | Valor por defecto |
|---|---|
| Timeout por solicitud | 15 s |
| Reintentos adicionales | 3 (hasta 4 solicitudes en total) |
| Espera entre reintentos | creciente: 1 s, 2 s, 4 s... (+ jitter), máximo 30 s |
| robots.txt | se lee antes de pedir; si no se puede leer, no se pide |
| User-Agent | honesto; definir `SCRAPER_CONTACTO` |

## 4. Formato de reporte de errores
`ok:false` con uno de 4 valores en `error`, y `detalle` opcional con el texto técnico:

| error | Significa | Qué hace A |
|---|---|---|
| `timeout` | sin conexión, timeout o servidor caído tras los reintentos | marcar la fuente como `fallo`, conservar el último dato bueno con su fecha |
| `bloqueo` | 403/429/captcha o robots.txt no lo permite | igual; no reintentar a mano |
| `formato_cambiado` | el dato esperado no apareció | igual; revisar el log |
| `no_encontrado` | el juego no existe en la fuente o no hay coincidencia segura | igual; es normal en algunos juegos |

Todo resultado trae `fecha_consulta` (UTC). Cada fallo queda también en el log (juego, fuente, tipo de error, hora).

## 5. Apagar una fuente a propósito (para la prueba de defensa y para las mediciones)
    $env:TIENDAS_DESACTIVADAS = "Epic Games Store"
Una tienda apagada no consume solicitudes ni ensucia el log. Para la demostración de "¿qué pasa si un sitio falla?", apagar
otra (ej. `Eneba`) y mostrar que el proceso sigue y que el sitio conserva el dato anterior con su fecha.

## 6. Fórmula del indicador de oferta (la aplica A en la consolidación)
Ver sección 3.3 de `Documentacion_B.md`. Se calcula en `Oferta/CalculadorOferta.cs` (`Consolidar`).
