#!/usr/bin/env python3
"""
Genera un catalogo.json DE EJEMPLO (datos inventados) siguiendo el Contrato 2.

Lo usa la Persona C para desarrollar y probar el sitio y la actualización automática
mientras el orquestador de A no está listo. Los NOMBRES, plataformas, fechas y precio
sugerido salen del REPO real de B (ScraperJuegos/Data/juegos.csv); los precios, scores
y horas son aleatorios. Cuando A tenga su comando, el workflow pasa a modo "real" y este
script deja de usarse (no hay que tocar el sitio).

Uso:
  python3 scripts/generar_catalogo_mock.py --repo ScraperJuegos/Data/juegos.csv --salida catalogo.json [--max 30]
"""
import argparse
import csv
import json
import random
import sys
from datetime import datetime, timedelta, timezone
from urllib.parse import quote

# Algunos appid de Steam conocidos, solo para que el mock tenga imágenes reales.
STEAM_APPIDS = {
    "elden ring": 1245620, "cyberpunk 2077": 1091500, "baldur's gate 3": 1086940,
    "hades": 1145360, "hades ii": 1145350, "stardew valley": 413150, "hollow knight": 367520,
    "red dead redemption 2": 1174180, "god of war": 1593500, "celeste": 504230,
    "terraria": 105600, "the witcher 3: wild hunt": 292030, "sekiro: shadows die twice": 814380,
    "monster hunter: world": 582010, "dark souls iii": 374320, "portal 2": 620,
    "doom eternal": 782330, "resident evil 4": 2050650, "lies of p": 1627720, "palworld": 1623730,
    "hogwarts legacy": 990080, "it takes two": 1426210, "black myth: wukong": 2358720,
    "monster hunter wilds": 2246340, "sea of stars": 1244090, "cuphead": 268910,
    # (lista ampliada para que la vista previa tenga imágenes; en modo real las imágenes vienen de los scrapers de B)
    "grand theft auto v": 271590, "death stranding": 1190460, "control": 870780, "monster hunter rise": 1446780,
    "diablo iv": 2344520, "forza horizon 5": 1551360, "forza horizon 4": 1293830, "halo infinite": 1240440,
    "microsoft flight simulator": 1250410, "assassin's creed valhalla": 2208920, "star wars jedi: fallen order": 1172380,
    "star wars jedi: survivor": 1774580, "dead space": 1693980, "armored core vi: fires of rubicon": 1888160, "sifu": 2138710,
    "undertale": 391540, "dead cells": 588650, "vampire survivors": 1794680, "hi-fi rush": 1817230, "cult of the lamb": 1313140,
    "disco elysium": 632470, "outer wilds": 753640, "subnautica": 264710, "slay the spire": 646570,
    "return of the obra dinn": 653530, "inside": 304430, "a way out": 1222700, "half-life: alyx": 546560,
    "left 4 dead 2": 550, "doom": 379720, "wolfenstein ii: the new colossus": 612880, "fallout 4": 377160,
    "the elder scrolls v: skyrim": 489830, "fallout: new vegas": 22380, "mass effect legendary edition": 1328670,
    "dragon age: inquisition": 1222690, "yakuza: like a dragon": 1235140, "sonic frontiers": 1237320,
    "crash bandicoot n. sane trilogy": 731490, "spyro reignited trilogy": 996580, "shadow of the tomb raider": 750920,
    "metro exodus": 412020, "borderlands 3": 397540, "helldivers 2": 553850, "lethal company": 1966720, "balatro": 2379780,
    "ratchet & clank: rift apart": 1895880, "octopath traveler": 921570, "hollow knight: silksong": 1030300,
    "clair obscur: expedition 33": 1903340, "kingdom come: deliverance ii": 1771300, "persona 5 royal": 1687950,
    "final fantasy vii remake": 1462040, "resident evil 2": 883710, "resident evil 3": 952060, "devil may cry 5": 601150,
    "nier: automata": 524220, "persona 4 golden": 1113000, "persona 3 reload": 2161700, "rise of the tomb raider": 391220,
    "horizon zero dawn": 1151640, "days gone": 1259420, "marvel's spider-man": 1817070,
    "marvel's spider-man: miles morales": 1817190, "dark souls: remastered": 570940,
    "dark souls ii: scholar of the first sin": 335300, "nioh 2": 1325200, "cities: skylines": 255710,
    "sid meier's civilization vi": 289070, "xcom 2": 268500, "total war: warhammer iii": 1142710, "stray": 1332010,
    "little nightmares ii": 860510, "ori and the will of the wisps": 1057090, "ori and the blind forest": 387290,
    "hellblade: senua's sacrifice": 414340, "a plague tale: requiem": 1182900, "dishonored 2": 403640,
    "bioshock infinite": 8870, "hitman 3": 1659040, "assassin's creed odyssey": 812140, "assassin's creed origins": 582160,
    "sea of thieves": 1172620, "valheim": 892970, "satisfactory": 526870, "factorio": 427520, "rimworld": 294100,
    "katana zero": 460950, "dave the diver": 1868140, "tunic": 553420, "spelunky 2": 418530, "risk of rain 2": 632360,
    "among us": 945360, "mortal kombat 11": 976310, "tekken 7": 389730, "dragon ball fighterz": 678950,
    "guilty gear -strive-": 1384160, "battlefield 2042": 1517290, "battlefield v": 1238810,
    "need for speed unbound": 1846380, "sonic mania": 584400, "tales of arise": 740130, "final fantasy xv": 637650,
    "ghostwire: tokyo": 1475810, "deathloop": 1252330, "star wars battlefront ii": 1237950,
    "warhammer 40,000: space marine 2": 2183900, "mafia: definitive edition": 1030840,
    "dying light 2 stay human": 534380, "the outer worlds": 578650, "divinity: original sin 2": 435150,
    "firewatch": 383870, "what remains of edith finch": 501300, "gris": 683320, "untitled goose game": 837470,
    "shovel knight: treasure trove": 250760, "cocoon": 1497440, "pentiment": 1205520, "neon white": 1533420,
    "street fighter 6": 1364780, "tekken 8": 1778820, "mortal kombat 1": 1971870, "final fantasy xvi": 2515020,
    "dragon's dogma 2": 2054970, "starfield": 1716740, "god of war ragnarök": 2322010,
    "the last of us part i": 1888930, "horizon forbidden west": 2420110, "ghost of tsushima": 2215430,
    "resident evil village": 1196590, "final fantasy vii rebirth": 2909400, "elden ring nightreign": 2622380,
    "returnal": 1649240, "uncharted: legacy of thieves collection": 1659420, "wo long: fallen dynasty": 1448440,
    "lords of the fallen": 1501750, "remnant ii": 1282100, "metaphor: refantazio": 2679460, "silent hill 2": 2124490,
    "prince of persia: the lost crown": 2751000, "indiana jones and the great circle": 2677660, "avowed": 2457220,
    "doom: the dark ages": 3017860, "split fiction": 2001120, "persona 5 tactica": 2254740,
    "octopath traveler ii": 1971650, "sonic x shadow generations": 2513280, "f1 24": 2488620,
    "triangle strategy": 1850510, "like a dragon: infinite wealth": 2072450,
}

# Qué tienda vende cada plataforma (igual que los scrapers de B):
# PC = Steam + Epic Games Store (vía CheapShark) + Eneba; Switch = Nintendo eShop + Eneba;
# PS4/PS5/Xbox = solo Eneba (PlayStation Store se eliminó del proyecto).
TIENDAS_POR_PLATAFORMA = {
    "PC": ["Steam", "Epic Games Store", "Eneba"],
    "PS5": ["Eneba"],
    "PS4": ["Eneba"],
    "Switch": ["Nintendo eShop", "Eneba"],
    "Xbox Series": ["Eneba"],
    "Xbox One": ["Eneba"],
}


def enlace(tienda, nombre, appid):
    q = quote(nombre)
    if tienda == "Steam":
        return f"https://store.steampowered.com/app/{appid}/" if appid else f"https://store.steampowered.com/search/?term={q}"
    return {
        "Epic Games Store": f"https://www.cheapshark.com/search#q:{q}",
        "Nintendo eShop": f"https://www.nintendo.com/us/search/#q={q}",
        "Eneba": f"https://www.eneba.com/store?text={q}",
    }[tienda]


def pct(regular, actual):
    return round((regular - actual) / regular * 100, 2)


def evaluar(actual, regular, sugerido):
    """Mismo criterio que CalculadorOferta.cs (B)."""
    if regular and regular > 0:
        return (True, pct(regular, actual)) if actual < regular - 0.01 else (False, 0.0)
    if sugerido and sugerido > 0 and actual < sugerido - 0.01:
        return True, pct(sugerido, actual)
    return False, 0.0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--repo", required=True)
    ap.add_argument("--salida", required=True)
    ap.add_argument("--max", type=int, default=0, help="0 = todos los juegos del REPO")
    a = ap.parse_args()

    with open(a.repo, encoding="utf-8-sig", newline="") as f:
        filas = list(csv.DictReader(f, delimiter=";"))

    juegos = {}
    for r in filas:
        clave = r["nombre"].strip().lower()
        juegos.setdefault(clave, []).append(r)

    ahora = datetime.now(timezone.utc).replace(microsecond=0)
    rnd = random.Random(ahora.strftime("%Y%m%d%H"))  # cambia en cada corrida (por hora)
    salida = []
    for i, (clave, grupo) in enumerate(juegos.items(), start=1):
        if a.max and i > a.max:
            break
        base = grupo[0]
        nombre = base["nombre"].strip()
        plataformas = list(dict.fromkeys(g["plataforma"].strip() for g in grupo))
        sugerido = float(base["precio_sugerido"].replace(",", ".") or 0)
        appid = base.get("steam_appid") or STEAM_APPIDS.get(clave)
        imagen = base.get("imagen_url") or (f"https://cdn.cloudflare.steamstatic.com/steam/apps/{appid}/header.jpg" if appid else None)

        tiendas, estado = [], {}
        for plat in plataformas:
            for t in TIENDAS_POR_PLATAFORMA.get(plat, []):
                falla = rnd.random() < 0.07
                estado[t] = "fallo" if falla and estado.get(t) != "ok" else "ok"
                if falla and rnd.random() < 0.5:
                    continue  # fuente caída y sin dato anterior: no hay precio de esa tienda
                regular = sugerido if sugerido > 0 else round(rnd.choice([19.99, 29.99, 39.99, 59.99]), 2)
                if t == "Eneba":
                    regular = round(regular * rnd.uniform(0.75, 0.95), 2)
                actual = regular
                if rnd.random() < 0.18:
                    actual = round(regular * (1 - rnd.choice([0.1, 0.2, 0.25, 0.33, 0.4, 0.5, 0.6, 0.75])), 2)
                consulta = ahora - (timedelta(days=rnd.randint(1, 3)) if falla else timedelta(minutes=rnd.randint(0, 40)))
                tiendas.append({
                    "tienda": t, "precio_actual": actual, "precio_regular": regular,
                    "formato": "digital",  # las tiendas de B solo venden digital (limitación 3.6 de B)
                    "plataforma": plat, "enlace": enlace(t, nombre, appid if t == "Steam" else None),
                    "fecha_consulta": consulta.isoformat(),
                })

        mejor = None
        for o in tiendas:
            ofe, d = evaluar(o["precio_actual"], o["precio_regular"], sugerido)
            if mejor is None or (o["precio_actual"], -d) < (mejor[0]["precio_actual"], -mejor[2]):
                mejor = (o, ofe, d)

        meta_ok = rnd.random() > 0.12
        hltb_ok = rnd.random() > 0.15
        estado["metacritic"] = "ok" if meta_ok else "fallo"
        estado["hltb"] = "ok" if hltb_ok else "fallo"
        salida.append({
            "id": i,
            "nombre": nombre,
            "imagen_url": imagen,
            "plataformas": plataformas,
            "fecha_lanzamiento": base["fecha_lanzamiento"],
            "precio_sugerido": sugerido or None,
            "tiendas": tiendas,
            "mejor_precio": mejor[0]["precio_actual"] if mejor else None,
            "mejor_tienda": mejor[0]["tienda"] if mejor else None,
            "descuento_pct": mejor[2] if mejor else 0,
            "en_oferta": mejor[1] if mejor else False,
            "metacritic": {"score": rnd.randint(58, 97), "plataforma": plataformas[0]} if meta_ok else None,
            "hltb_horas": round(rnd.uniform(4, 120), 1) if hltb_ok else None,
            "ultima_actualizacion": ahora.isoformat(),
            "estado_fuentes": estado,
        })

    with open(a.salida, "w", encoding="utf-8") as f:
        json.dump(salida, f, ensure_ascii=False, indent=1)
    print(f"Mock escrito: {a.salida} ({len(salida)} juegos)")


if __name__ == "__main__":
    sys.exit(main())
