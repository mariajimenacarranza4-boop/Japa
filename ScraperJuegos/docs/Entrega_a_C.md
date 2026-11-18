# Entrega de B a C — valores válidos para los filtros e imágenes

## 1. Valores válidos (salen de `Catalogos.cs`; se exportan con `dotnet run -- catalogos`)
| Filtro | Valores |
|---|---|
| Plataforma | PS5, PS4, Xbox Series, Xbox One, PC, Switch |
| Formato | digital, fisico |
| Tienda | Steam, Epic Games Store, Nintendo eShop, Eneba |

Plataformas en el REPO actual (filas): PC 194, Switch 121, PS5 95, PS4 92, Xbox One 81, Xbox Series 77.

## 2. Avisos para armar la interfaz
- **Formato físico:** las tiendas implementadas solo venden digital, así que "fisico" no tendrá resultados. El filtro puede existir,
  pero hay que mostrar "sin resultados" y no un error (la limitación está en 3.6 de `Documentacion_B.md`).
- **Epic Games Store** se consulta vía CheapShark; si CheapShark no responde o bloquea, no aparece ningún precio de esa tienda. Mostrar el filtro solo con las tiendas
  que tengan datos, o dejarlo sin resultados sin romper nada.
- **Un juego es una fila por plataforma** en el REPO (660 filas, 249 juegos distintos). Para la tarjeta de un juego con varias
  consolas, agrupar por nombre normalizado y mostrar las consolas como etiquetas.
- **Datos faltantes:** si un juego no tiene Metacritic u HLTB, mostrar "N/D" (los campos llegan sin valor porque el JSON omite los nulos).
- **Cobertura de precios:** PC = Steam + Epic (vía CheapShark) + Eneba; Switch = Nintendo eShop + Eneba; PS4/PS5/Xbox = solo Eneba.
  Hay juegos con una sola tienda; la tarjeta debe funcionar igual (mejor precio entre las que hay).
- **Atribución obligatoria:** mostrar "Fuente: HowLongToBeat" donde aparezca el tiempo de juego.

## 3. Imágenes (`imagen_url`)
- La columna `imagen_url` del REPO está vacía en esta versión; la imagen sale de la respuesta de cada tienda
  (`ResultadoTienda.ImagenUrl`; Steam usa `header_image`).
- Regla: usar la imagen de la primera tienda que la traiga; si ninguna responde, usar `imagen_url` del REPO; si tampoco hay, mostrar un
  marcador de posición.
- HLTB también puede traer imagen (`ResultadoHltb.ImagenUrl`) como último respaldo.
- Las imágenes se enlazan (hotlink) desde el sitio de origen; no se copian ni se almacenan.

## 4. Campos del JSON de cada fuente (snake_case, sin nulos)
- Tienda: `ok, tienda, precio_actual, precio_regular, formato, plataforma, enlace, imagen_url, moneda, fecha_consulta`
- Metacritic: `ok, score, plataforma, verificada, enlace, fecha_lanzamiento, fecha_consulta`
- HLTB: `ok, horas_completo, horas_principal, horas_extra, horas_todos_los_estilos, enlace, fuente, fecha_consulta` y extras opcionales
  (desarrollador, plataformas, anio_lanzamiento, calificacion_usuarios, imagen_url...)
- El **formato final por juego** que lee C es el Contrato 2 (lo escribe A en `catalogo`).
