#!/usr/bin/env python3
"""
Resume el log de fallos de la última corrida (A_Orquestador/log_fallos.txt, formato fecha;juego;plataforma;fuente;tipo;detalle)
en un archivo corto y legible: cuántos fallos hubo por fuente y tipo, con 3 ejemplos de cada uno.
Se guarda en web/data/fallos_ultima_corrida.txt para poder revisar desde el repositorio por qué falló una fuente.

Uso: python3 scripts/resumir_fallos.py A_Orquestador/log_fallos.txt web/data/fallos_ultima_corrida.txt
"""
import collections
import sys
from datetime import datetime, timezone


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        return 1
    origen, destino = sys.argv[1], sys.argv[2]
    grupos = collections.OrderedDict()
    total = 0
    try:
        with open(origen, encoding="utf-8-sig", errors="replace") as f:
            for linea in f:
                p = linea.rstrip("\n").split(";", 5)
                if len(p) < 5 or p[5:6] == ["orquestador"]:
                    continue  # líneas resumen del orquestador: repiten el fallo del scraper
                fuente = p[3].split("/")[0].strip() or "?"
                tipo = p[4].strip() or "?"
                clave = (fuente, tipo)
                g = grupos.setdefault(clave, {"n": 0, "ejemplos": []})
                g["n"] += 1
                total += 1
                if len(g["ejemplos"]) < 3:
                    g["ejemplos"].append(f"{p[1]} [{p[2]}]: {(p[5] if len(p) > 5 else '')[:300]}")
    except FileNotFoundError:
        grupos = {}

    ahora = datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M UTC")
    salida = [f"Fallos de la última corrida ({ahora}) — total: {total}", ""]
    for (fuente, tipo), g in sorted(grupos.items(), key=lambda kv: -kv[1]["n"]):
        salida.append(f"{fuente:<22} {tipo:<16} {g['n']:>5}")
        for e in g["ejemplos"]:
            salida.append(f"    - {e}")
    if not grupos:
        salida.append("(sin log de fallos: la corrida no generó log_fallos.txt)")
    with open(destino, "w", encoding="utf-8") as f:
        f.write("\n".join(salida) + "\n")
    print("\n".join(salida[:40]))
    return 0


if __name__ == "__main__":
    sys.exit(main())
