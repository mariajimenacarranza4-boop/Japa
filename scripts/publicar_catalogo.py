#!/usr/bin/env python3
"""
Valida el catálogo que produjo una corrida (de A o el mock) y SOLO si es válido lo copia
a web/data/catalogo.json. Además agrega la corrida a web/data/ejecuciones.json, que es la
evidencia de las ejecuciones automáticas (el sitio la muestra en el pie de página).

Reglas de protección (para que la página nunca quede con datos rotos o vacíos):
  1. El archivo debe ser JSON válido: una lista de registros del Contrato 2
     (o un objeto con la clave "juegos").
  2. Cada registro debe traer al menos: id, nombre, plataformas, tiendas, ultima_actualizacion.
  3. Debe haber al menos --min-juegos registros.
  4. Si la corrida trae menos de la mitad de juegos que la publicación anterior, se rechaza
     (casi seguro es una corrida fallida, no un REPO que se achicó).
  5. Si NINGÚN juego tiene precio, o los juegos con precio bajan a menos de la mitad que antes, se rechaza
     (por ejemplo, si los sitios bloquearon al servidor de GitHub: es mejor seguir mostrando el último dato bueno).
Además, con --repo, se QUITAN los juegos que ya no están en el REPO (el orquestador de A conserva los registros viejos
para no perder datos en corridas parciales; sin esto, un juego borrado del CSV seguiría apareciendo para siempre).
Con --podar-origen también se limpia el archivo de trabajo de A (datos/catalogo_a.json).

Si algo falla, NO se toca catalogo.json (el sitio sigue mostrando el último dato bueno),
se registra la corrida como "rechazada" y el script sale con código 1 para que el
workflow quede en rojo y se note.

Uso:
  python3 scripts/publicar_catalogo.py --nuevo salida/catalogo.json --destino web/data \
      --modo real --evento schedule --run-id 123 --run-url https://...
"""
import argparse
import csv
import json
import os
import sys
from datetime import datetime, timezone

OBLIGATORIOS = ("id", "nombre", "plataformas", "tiendas", "ultima_actualizacion")
MAX_HISTORIAL = 100


def cargar_registros(ruta):
    with open(ruta, encoding="utf-8-sig") as f:
        datos = json.load(f)
    if isinstance(datos, dict):
        datos = datos.get("juegos")
    if not isinstance(datos, list):
        raise ValueError("el catálogo no es una lista de juegos (ni un objeto con 'juegos')")
    return datos


def nombres_del_repo(ruta_csv):
    """Nombres de juego del REPO (minúsculas, sin espacios extra)."""
    with open(ruta_csv, encoding="utf-8-sig", newline="") as f:
        return {(r.get("nombre") or "").strip().lower() for r in csv.DictReader(f, delimiter=";") if r.get("nombre")}


def con_precio(registros):
    return sum(1 for r in registros if isinstance(r, dict) and r.get("mejor_precio") is not None)


def validar(registros, min_juegos, previos, previos_con_precio=0):
    errores = []
    for i, r in enumerate(registros):
        if not isinstance(r, dict):
            errores.append(f"registro {i}: no es un objeto")
            continue
        faltan = [k for k in OBLIGATORIOS if k not in r]
        if faltan:
            errores.append(f"registro {i} ({r.get('nombre', '?')}): faltan {', '.join(faltan)}")
        if not isinstance(r.get("tiendas", []), list):
            errores.append(f"registro {i}: 'tiendas' no es lista")
    ids = [r.get("id") for r in registros if isinstance(r, dict)]
    if len(ids) != len(set(ids)):
        errores.append("hay ids repetidos")
    if len(registros) < min_juegos:
        errores.append(f"solo {len(registros)} juegos (mínimo {min_juegos})")
    if previos and len(registros) < previos / 2:
        errores.append(f"bajó de {previos} a {len(registros)} juegos: se considera una corrida fallida")
    cp = con_precio(registros)
    if registros and cp == 0:
        errores.append("ningún juego tiene precio: todas las tiendas fallaron (¿bloqueo o sin red?)")
    elif previos_con_precio and cp < previos_con_precio / 2:
        errores.append(f"juegos con precio bajaron de {previos_con_precio} a {cp}: se considera una corrida fallida")
    return errores


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--nuevo", required=True)
    ap.add_argument("--destino", default="web/data")
    ap.add_argument("--min-juegos", type=int, default=1)
    ap.add_argument("--modo", default="desconocido")
    ap.add_argument("--evento", default="manual")
    ap.add_argument("--run-id", default="")
    ap.add_argument("--run-url", default="")
    ap.add_argument("--duracion-seg", type=float, default=None)
    ap.add_argument("--repo", default=None, help="CSV del REPO: se quitan los juegos que ya no están en él")
    ap.add_argument("--podar-origen", action="store_true", help="también reescribe --nuevo sin esos juegos")
    a = ap.parse_args()

    ruta_cat = os.path.join(a.destino, "catalogo.json")
    ruta_hist = os.path.join(a.destino, "ejecuciones.json")
    os.makedirs(a.destino, exist_ok=True)

    previos = previos_cp = 0
    previo_modo = None
    if os.path.exists(ruta_cat):
        try:
            anteriores = cargar_registros(ruta_cat)
            previos, previos_cp = len(anteriores), con_precio(anteriores)
            with open(ruta_cat, encoding="utf-8-sig") as f:
                d = json.load(f)
            previo_modo = d.get("modo") if isinstance(d, dict) else None
        except Exception:
            previos = previos_cp = 0
    # Al pasar de datos de ejemplo (mock) a datos reales, no se compara contra el mock.
    if previo_modo == "mock" and a.modo != "mock":
        previos = previos_cp = 0

    registros, errores = [], []
    try:
        registros = cargar_registros(a.nuevo)
        if a.repo:
            en_repo = nombres_del_repo(a.repo)
            quitados = [r.get("nombre") for r in registros if isinstance(r, dict) and (r.get("nombre") or "").strip().lower() not in en_repo]
            if quitados:
                registros = [r for r in registros if isinstance(r, dict) and (r.get("nombre") or "").strip().lower() in en_repo]
                print(f"Se quitan {len(quitados)} juegos que ya no están en el REPO: {', '.join(map(str, quitados[:20]))}")
                if a.podar_origen:
                    tmp = a.nuevo + ".tmp"
                    with open(tmp, "w", encoding="utf-8") as f:
                        json.dump(registros, f, ensure_ascii=False, indent=2)
                    os.replace(tmp, a.nuevo)
        errores = validar(registros, a.min_juegos, previos, previos_cp)
    except FileNotFoundError:
        errores = [f"no existe {a.nuevo} (¿el comando de scraping no generó el archivo?)"]
    except Exception as e:
        errores = [f"JSON inválido: {e}"]

    ahora = datetime.now(timezone.utc).replace(microsecond=0).isoformat()
    ok = not errores
    if ok:
        publicado = {
            "generado": ahora,
            "modo": a.modo,
            "total": len(registros),
            "juegos": registros,
        }
        tmp = ruta_cat + ".tmp"
        with open(tmp, "w", encoding="utf-8") as f:
            json.dump(publicado, f, ensure_ascii=False, separators=(",", ":"))
        os.replace(tmp, ruta_cat)  # reemplazo atómico: nunca queda un archivo a medias

    historial = []
    if os.path.exists(ruta_hist):
        try:
            with open(ruta_hist, encoding="utf-8") as f:
                historial = json.load(f)
        except Exception:
            historial = []
    historial.insert(0, {
        "fecha": ahora,
        "evento": a.evento,            # schedule = automática, workflow_dispatch = manual
        "modo": a.modo,                # real | mock
        "resultado": "publicada" if ok else "rechazada",
        "juegos": len(registros),
        "con_precio": con_precio(registros),
        "duracion_seg": a.duracion_seg,
        "run_id": a.run_id,
        "run_url": a.run_url,
        "errores": errores[:10],
    })
    with open(ruta_hist, "w", encoding="utf-8") as f:
        json.dump(historial[:MAX_HISTORIAL], f, ensure_ascii=False, indent=1)

    if ok:
        print(f"Publicado: {ruta_cat} ({len(registros)} juegos, modo {a.modo})")
        return 0
    print("Catálogo RECHAZADO; se mantiene la publicación anterior. Motivos:")
    for e in errores[:20]:
        print("  - " + e)
    return 1


if __name__ == "__main__":
    sys.exit(main())
