# Pasos para dejar todo funcionando (Persona B) — en orden

Ejecutar desde PowerShell, dentro de la carpeta del proyecto. Requisito: .NET 8 SDK.
Recomendado: definir su correo para el User-Agent honesto (lo piden los sitios y Wikimedia):

    $env:SCRAPER_CONTACTO = "su-correo@estudiantec.cr"

## 1. Compilar y probar sin internet
    dotnet build
    dotnet run -- autotest
Debe terminar con "0 fallidas" (143 pruebas). Si algo sale en rojo, copiar el texto completo.

## 2. Completar los hltb_id (HLTB no permite su buscador; solo /game/ID)
    dotnet run -- hltb-autocompletar --aplicar
Tarda unos minutos (1 solicitud por segundo). Descubre los ids en Wikidata y los copia a Data/juegos.csv.
Los que no encuentre quedan en salida/hltb_autocompletar.csv (con enlace de búsqueda de HLTB): llenar a mano la columna
hltb_id_a_llenar (acepta el número o la URL) e importar:
    dotnet run -- hltb-importar --archivo salida\hltb_autocompletar.csv
Luego verificar CADA id contra la página real de HLTB (detecta ids que pertenecen a otro juego):
    dotnet run -- hltb-verificar
Los ids que apunten a OTRO juego (ej. Shovel Knight: Treasure Trove) se vacían solos con:
    dotnet run -- hltb-verificar --limpiar
(quedan en N/D; si quiere, busque el juego en HLTB, copie el número de la URL a hltb_id y repita hltb-verificar).

## 2b. Verificar las fechas de lanzamiento del REPO
Varias filas tienen la misma fecha en todas las plataformas (ej. God of War en PC). Metacritic publica la fecha por plataforma:
    dotnet run -- fechas-verificar 20            # prueba con 20 filas
    dotnet run -- fechas-verificar               # todo el REPO (unos 11 min, 1 solicitud/s)
Revisar salida/fechas_verificar.csv (estados: igual, distinta, sin_fecha). Con --aplicar se corrigen en Data/juegos.csv solo las
filas donde Metacritic confirmó la plataforma. Los precios sugeridos siguen siendo un precio de referencia general.

## 3. Epic Games Store ahora se consulta VÍA CHEAPSHARK (ya no hay que apagarla)
store.epicgames.com responde 403 a los clientes automáticos, así que Epic se lee de la API pública y gratuita de CheapShark
(sin clave). Esto se probó solo con datos simulados (143 pruebas); falta la prueba contra el sitio real, que le toca a B:

    dotnet run -- probar 10
En los juegos de PC debe salir un precio de "Epic Games Store" (no todos los juegos están en Epic: "no_encontrado" en algunos es normal).
Los enlaces de Epic empiezan con https://www.cheapshark.com/redirect?dealID=... : abra uno en el navegador y confirme que lleva a la
ficha del juego en Epic (CheapShark pide usar sus enlaces /redirect).

    dotnet run -- diagnostico
En la sección "Epic vía CheapShark" las rutas /api/1.0/stores y /api/1.0/deals deben salir PERMITIDO. Si CheapShark no permitiera /api,
el scraper lo marca como `bloqueo`, deja de insistir unos minutos y Epic queda sin precios, sin romper nada (el resto sigue normal).
El archivo salida/diagnostico/cheapshark_tiendas.json muestra el id que CheapShark le da a Epic y si está activa (isActive).

Si algo falla:
- `formato_cambiado` con "ya no lista" o "inactiva": CheapShark dejó de seguir a Epic; apagar solo esa tienda:
      $env:TIENDAS_DESACTIVADAS = "Epic Games Store"
- `bloqueo` por 429: mucha frecuencia; esperar unos minutos y repetir con menos juegos (no hay que subir la velocidad: CheapShark advierte
  que las ráfagas automáticas pueden terminar en bloqueo permanente).
- Para ahorrar la solicitud de /stores o si cambia el id: `$env:CHEAPSHARK_EPIC_STORE_ID = "<id de cheapshark_tiendas.json>"`.

### 3b. RESPALDO para Epic: IsThereAnyDeal (API oficial) — recomendado si CheapShark falla
Si CheapShark da `bloqueo`, `formato_cambiado` o no encuentra el juego, Epic se consulta automáticamente con la API oficial de
IsThereAnyDeal (ITAD), que sí cubre los precios de Epic. Solo necesita una clave GRATUITA:
1. Entrar a https://isthereanydeal.com/apps/ con una cuenta (gratis), crear una "app" y copiar su API key.
2. En PowerShell (cada vez que abra la ventana; la clave NO se guarda en el proyecto ni se sube al repositorio):

       $env:ITAD_API_KEY = "su-clave-aqui"
       dotnet run -- probar 10 --salida salida
3. En los juegos de PC debe salir un precio de "Epic Games Store"; si vino de ITAD, el enlace empieza con https://isthereanydeal.com/...
4. `dotnet run -- diagnostico --salida salida` ahora incluye la fila "Epic vía ITAD (respaldo)": anotar también su fecha en la tabla 3.5b.
Sin clave el respaldo no hace ninguna solicitud y el mensaje de error de Epic dice "defina ITAD_API_KEY".
Límite de ITAD: 1000 solicitudes cada 5 minutos (el programa usa ~300 con pausa de 1 s). Si la clave es inválida, el mensaje dice
"revise ITAD_API_KEY". Opcional: `$env:ITAD_EPIC_SHOP_ID = "<número>"` para omitir la consulta de tiendas.
(PlayStation Store se eliminó del proyecto.)

## 4. Probar contra los sitios reales
    dotnet run -- diagnostico
    dotnet run -- probar 10
Revisar cada fuente (Steam, Epic vía CheapShark, Nintendo, Eneba, Metacritic, HLTB).
Si una fuente falla con formato_cambiado o bloqueo, enviar la salida de la consola y salida/diagnostico/informe.txt.

## 5. Prueba de defensa (REPO dinámico)
Agregar una fila a Data/juegos.csv y correr:
    dotnet run -- probar --juego "ese juego"
Debe aparecer sin tocar código.

## 6. Antes de entregar
- Anotar en docs/Documentacion_B.md la fecha de revisión de robots.txt/términos de cada sitio, en la TABLA de la sección 3.5b
  (columna "Fecha revisada"), incluida la fila nueva de **CheapShark**: el día en que corrió `diagnostico` (la fecha sale en la primera
  línea de salida/diagnostico/informe.txt) y la conclusión (PERMITIDO/BLOQUEADO para /api/1.0/...). Adjuntar el informe.txt.
- Revisar la página de términos/uso de CheapShark (https://apidocs.cheapshark.com/) y la de ITAD (https://docs.isthereanydeal.com/) y anotar la fecha.
- Citar a HowLongToBeat como fuente en el sitio y en el informe.
- No subir claves: .gitignore ya excluye .env, *.key, secrets* y salida/.
