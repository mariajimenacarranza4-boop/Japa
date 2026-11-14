# III Proyecto: Web Scraping Multinúcleo y Sitio Web

IC3101 Arquitectura de Computadoras · TEC San Carlos

**Sitio público:** https://USUARIO.github.io/REPOSITORIO/ *(reemplazar)*
**Documento técnico:** [docs/Documentacion_Tecnica.md](docs/Documentacion_Tecnica.md) · **Guion de la demo:** [docs/Guion_Demo.md](docs/Guion_Demo.md)

[![Actualizar catálogo](../../actions/workflows/actualizar-catalogo.yml/badge.svg)](../../actions/workflows/actualizar-catalogo.yml)

| Carpeta | Contenido | Responsable |
|---|---|---|
| `ScraperJuegos/` | C# .NET 8: REPO (`Data/juegos.csv`) y scrapers | B |
| `A_Orquestador/` | C# .NET 8: orquestador paralelo en 3 niveles y mediciones | A |
| `datos/` | Archivo de trabajo de A (último dato bueno por fuente) | automático |
| `web/` | Sitio "JAPA" (HTML/CSS/JS, sin compilación) | C |
| `scripts/` | Validación/publicación del catálogo y datos de ejemplo | C |
| `.github/workflows/` | Actualización automática cada 12 h y despliegue a GitHub Pages | C |
| `docs/` | Documentación técnica consolidada y guion de la demo | C (consolida) |

## Cómo funciona
```
cron (cada 12 h) → GitHub Actions → scraping paralelo (A) sobre el REPO (B)
                 → scripts/publicar_catalogo.py valida → web/data/catalogo.json → commit → GitHub Pages
```
Sin base de datos externa ni claves en el repositorio: el REPO y el catálogo viven en este repositorio y el
workflow usa el token temporal de GitHub. Detalles en la sección 4.2 del documento técnico.

## Probar el sitio en local
```bash
python3 scripts/generar_catalogo_mock.py --repo ScraperJuegos/Data/juegos.csv --salida /tmp/catalogo.json
python3 scripts/publicar_catalogo.py --nuevo /tmp/catalogo.json --destino web/data --modo mock --evento local
cd web && python3 -m http.server 8000      # http://localhost:8000
```

## Publicar (una sola vez)
Ver la sección 6.4 del documento técnico: Pages con fuente "GitHub Actions", permisos de escritura para los workflows,
variable `MODO_CATALOGO` y secreto `SCRAPER_CONTACTO`.
