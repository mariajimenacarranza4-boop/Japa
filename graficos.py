#!/usr/bin/env python3
"""Gráficos de tiempo, speedup y eficiencia + estimación de la Ley de Amdahl.

Uso:  python graficos.py resultados_real.csv [carpeta_salida]
Requiere: pip install matplotlib
Lee el CSV que genera A_Orquestador ("... medir") y escribe tiempo.png, speedup.png,
eficiencia.png y amdahl.txt en la carpeta de salida (por defecto, junto al CSV).
"""
import csv
import os
import sys

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt


def num(x):
    try:
        return float(x)
    except (TypeError, ValueError):
        return None


def leer(ruta):
    with open(ruta, newline="", encoding="utf-8-sig") as f:
        filas = list(csv.DictReader(f))
    sec = next(float(r["tiempo_s"]) for r in filas if r["modo"] == "Secuencial")
    modos = {}
    for r in filas:
        if r["modo"] == "Secuencial":
            continue
        n, t = int(r["trabajadores"]), float(r["tiempo_s"])
        modos.setdefault(r["modo"], []).append((n, t, sec / t, sec / t / n * 100))
    for m in modos:
        modos[m].sort()
    return sec, modos


def grafico(modos, idx, titulo, ylabel, archivo, ref=None, sec=None):
    plt.figure(figsize=(7, 4.5))
    for m, pts in modos.items():
        plt.plot([p[0] for p in pts], [p[idx] for p in pts], marker="o", label=m)
    if sec is not None:
        plt.axhline(sec, ls="--", color="gray", label="Secuencial")
    if ref == "ideal":
        ns = sorted({p[0] for pts in modos.values() for p in pts})
        plt.plot(ns, ns, ls=":", color="black", label="Ideal (lineal)")
    if ref == "100":
        plt.axhline(100, ls=":", color="black", label="100 %")
    plt.xlabel("Trabajadores (nivel 1)")
    plt.ylabel(ylabel)
    plt.title(titulo)
    plt.grid(alpha=0.3)
    plt.legend()
    plt.tight_layout()
    plt.savefig(archivo, dpi=150)
    plt.close()


def amdahl(modos, sec):
    """Ley de Amdahl sobre el escalado del NIVEL 1 (trabajadores) dentro de cada modo.
    Speedup relativo S = T(1 trabajador, mismo modo) / T(n)  (si el modo no tiene n=1, se usa el secuencial).
    S = 1 / (f + (1-f)/n)  ->  p = (1 - 1/S) / (1 - 1/n);  f = 1 - p = fracción que no escala con los trabajadores."""
    lineas = ["Estimación de la Ley de Amdahl sobre los trabajadores del nivel 1 (f = fracción que no escala)",
              "S(n) = 1 / (f + (1-f)/n);  límite teórico = 1/f.  Speedup relativo al mismo modo con 1 trabajador.", ""]
    for m, pts in modos.items():
        base = next((t for n, t, _, _ in pts if n == 1), sec)
        lineas.append(f"{m}  (base = {base:.1f} s)")
        fs = []
        for n, t, _, _ in pts:
            s = base / t
            if n > 1 and s > 1:
                p = min((1 - 1 / s) / (1 - 1 / n), 1.0)
                fs.append(1 - p)
                lineas.append(f"  n={n:<3} S={s:5.2f}  f={1-p:5.3f}  límite 1/f={'inf' if p >= 1 else f'{1/(1-p):.2f}'}")
            else:
                lineas.append(f"  n={n:<3} S={s:5.2f}  (no sirve para estimar)")
        if fs:
            lineas.append(f"  -> f promedio = {sum(fs)/len(fs):.3f}")
        lineas.append("")
        if base != sec:
            # Si la base (1 trabajador del mismo modo) ya trae concurrencia extra de los niveles 2 y 3, no sirve para Amdahl:
            # se repite la estimación contra el secuencial real, que es la referencia del enunciado.
            lineas.append(f"{m}  (base = secuencial = {sec:.1f} s)")
            fs2 = []
            for n, t, _, _ in pts:
                s = sec / t
                if n > 1 and s > 1:
                    p = min((1 - 1 / s) / (1 - 1 / n), 1.0)
                    fs2.append(1 - p)
                    lineas.append(f"  n={n:<3} S={s:5.2f}  f={1-p:5.3f}  límite 1/f={'inf' if p >= 1 else f'{1/(1-p):.2f}'}")
                else:
                    lineas.append(f"  n={n:<3} S={s:5.2f}  (no sirve para estimar)")
            if fs2:
                lineas.append(f"  -> f promedio = {sum(fs2)/len(fs2):.3f}")
            lineas.append("")
    lineas.append("Nota: la parte 'secuencial' aquí incluye lo que limita el sitio (máx. simultáneas por dominio + pausa),")
    lineas.append("no solo código serie. Por eso f sube al aumentar n: el cuello de botella pasa a ser la red/los sitios.")
    return "\n".join(lineas)


def main():
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    ruta = sys.argv[1]
    out = sys.argv[2] if len(sys.argv) > 2 else os.path.dirname(os.path.abspath(ruta))
    os.makedirs(out, exist_ok=True)
    sec, modos = leer(ruta)
    grafico(modos, 1, "Tiempo total de scraping", "Tiempo (s)", os.path.join(out, "tiempo.png"), sec=sec)
    grafico(modos, 2, "Speedup = T_secuencial / T_paralelo", "Speedup", os.path.join(out, "speedup.png"), ref="ideal")
    grafico(modos, 3, "Eficiencia = speedup / n (>100 % con niveles 2/3: ver nota)", "Eficiencia (%)",
            os.path.join(out, "eficiencia.png"), ref="100")
    txt = amdahl(modos, sec)
    with open(os.path.join(out, "amdahl.txt"), "w", encoding="utf-8") as f:
        f.write(txt + "\n")
    print(txt)
    print(f"\nGuardado en {out}: tiempo.png, speedup.png, eficiencia.png, amdahl.txt")


if __name__ == "__main__":
    main()
