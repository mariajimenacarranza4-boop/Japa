# Pasos para dejar todo funcionando (Persona B) — en orden

Ejecutar desde PowerShell, dentro de la carpeta del proyecto. Requisito: .NET 8 SDK.
Recomendado: definir su correo para el User-Agent honesto (lo piden los sitios y Wikimedia):

    $env:SCRAPER_CONTACTO = "su-correo@estudiantec.cr"

## 1. Compilar y probar sin internet
    dotnet build
    dotnet run -- autotest
Debe terminar con "0 fallidas" (116 pruebas). Si algo sale en rojo, copiar el texto completo.

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

## 3. Epic Games Store ahora se consulta vía CheapShark (ya NO hay que apagarla)
La tienda directa de Epic responde 403; Epic se lee de la API pública de CheapShark (EpicCheapSharkScraper).
Revisar www.cheapshark.com/robots.txt y anotar la fecha en la sección 3.5b. Si CheapShark fallara, se puede apagar igual:
    $env:TIENDAS_DESACTIVADAS = "Epic Games Store"
(PlayStation Store se eliminó del proyecto.)

## 4. Probar contra los sitios reales
    dotnet run -- diagnostico
    dotnet run -- probar 10
Revisar cada fuente (Steam, Epic vía CheapShark, Nintendo, Eneba, Metacritic, HLTB). En diagnostico, "Epic Games Store (directo)" seguirá dando 403 (documentado); la que importa es "Epic vía CheapShark".
Si una fuente falla con formato_cambiado o bloqueo, enviar la salida de la consola y salida/diagnostico/informe.txt.

## 5. Prueba de defensa (REPO dinámico)
Agregar una fila a Data/juegos.csv y correr:
    dotnet run -- probar --juego "ese juego"
Debe aparecer sin tocar código.

## 6. Antes de entregar
- Anotar en docs/Documentacion_B.md la fecha de revisión de robots.txt/términos de cada sitio (sección 3.5).
- Citar a HowLongToBeat como fuente en el sitio y en el informe.
- No subir claves: .gitignore ya excluye .env, *.key, secrets* y salida/.
