/* JAPA — sitio de la Persona C.
 * Lee data/catalogo.json (Contrato 2, escrito por A) y lo muestra con filtros y orden combinados.
 * Sin dependencias ni compilación: GitHub Pages sirve esta carpeta tal cual.
 */
"use strict";

const ZONA = "America/Costa_Rica";
const ETIQUETA_FORMATO = { digital: "Digital", fisico: "Físico", otro: "Otro" };
const ND = "N/D";

const estado = { juegos: [], valores: null };
const $ = (id) => document.getElementById(id);

/* ---------- utilidades ---------- */
const num = (v) => (v === null || v === undefined || v === "" || isNaN(Number(v)) ? null : Number(v));
const dinero = (v) => (num(v) === null ? ND : "$" + num(v).toFixed(2));
const esc = (s) => String(s ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
const urlSegura = (u) => (/^https?:\/\//i.test(u || "") ? u : null);

function fecha(iso, conHora = true) {
  if (!iso) return ND;
  const d = new Date(iso);
  if (isNaN(d)) return ND;
  return d.toLocaleString("es-CR", {
    timeZone: ZONA, day: "2-digit", month: "short", year: "numeric",
    ...(conHora ? { hour: "2-digit", minute: "2-digit" } : {}),
  });
}

function hace(iso) {
  const d = new Date(iso);
  if (isNaN(d)) return "";
  const min = Math.round((Date.now() - d) / 60000);
  if (min < 1) return "hace un momento";
  if (min < 60) return `hace ${min} min`;
  const h = Math.round(min / 60);
  if (h < 48) return `hace ${h} h`;
  return `hace ${Math.round(h / 24)} días`;
}

/* Mismo criterio de oferta que CalculadorOferta.cs (B):
 * en oferta si precio_actual < precio_regular; si la tienda no da regular, se compara con el sugerido del REPO.
 * descuento % = (regular − actual) / regular × 100 */
function evaluarOferta(actual, regular, sugerido) {
  if (regular > 0) return actual < regular - 0.01 ? { en: true, pct: (regular - actual) / regular * 100, ref: regular } : { en: false, pct: 0, ref: regular };
  if (sugerido > 0 && actual < sugerido - 0.01) return { en: true, pct: (sugerido - actual) / sugerido * 100, ref: sugerido };
  return { en: false, pct: 0, ref: null };
}

/* Imágenes candidatas, de la mejor a la peor para una tarjeta horizontal:
 * Steam (cabecera horizontal) → Nintendo → imagen del REPO u otras → Eneba (portada cuadrada) → Epic/CheapShark (miniatura chica).
 * Si una no carga, la página prueba la siguiente; si ninguna carga, pone las iniciales del juego. */
function rangoImagen(u) {
  if (/steamstatic|steampowered/i.test(u)) return 0;
  if (/nintendo/i.test(u)) return 1;
  if (/eneba/i.test(u)) return 3;
  if (/cheapshark|epicgames/i.test(u)) return 4;
  return 2;
}
function imagenesOrdenadas(j, tiendas) {
  const todas = [j.imagen_url, ...tiendas.map((t) => t.imagen_url)].map(urlSegura).filter(Boolean);
  return [...new Set(todas)].sort((a, b) => rangoImagen(a) - rangoImagen(b));
}

/* Normaliza un registro del Contrato 2 sin fallar si faltan campos. */
function normalizar(j) {
  const tiendas = (Array.isArray(j.tiendas) ? j.tiendas : [])
    .filter((t) => t && t.ok !== false && num(t.precio_actual) !== null)
    .map((t) => ({ ...t, precio_actual: num(t.precio_actual), precio_regular: num(t.precio_regular), formato: (t.formato || "").toLowerCase() || null }));
  const formatos = new Set(tiendas.map((t) => t.formato).filter(Boolean));
  let tipo = "otro"; // físico o digital si TODAS sus ofertas son de ese formato; mezcla o sin dato = otro
  if (formatos.size === 1) tipo = [...formatos][0] === "fisico" ? "fisico" : [...formatos][0] === "digital" ? "digital" : "otro";
  const meta = j.metacritic && num(j.metacritic.score) !== null ? { score: num(j.metacritic.score), plataforma: j.metacritic.plataforma || null } : null;
  return {
    ...j,
    _imagenes: imagenesOrdenadas(j, tiendas),
    plataformas: Array.isArray(j.plataformas) ? j.plataformas : (j.plataformas ? [j.plataformas] : []),
    tiendas, tipo, meta,
    hltb: num(j.hltb_horas),
    sugerido: num(j.precio_sugerido),
    _nombre: (j.nombre || "").toLocaleLowerCase("es"),
  };
}

/* Ofertas que cumplen los filtros de tienda/formato/plataforma; el mejor precio se calcula SOBRE ellas,
 * así "filtrar por tienda X y ordenar por precio" ordena por el precio de X. */
function ofertasVisibles(j, f) {
  return j.tiendas.filter((t) =>
    (!f.tienda || t.tienda === f.tienda) &&
    (!f.formato || t.formato === f.formato) &&
    (!f.plataforma || !t.plataforma || t.plataforma === f.plataforma));
}

function mejorOferta(ofertas, sugerido) {
  let mejor = null;
  for (const t of ofertas) {
    const o = evaluarOferta(t.precio_actual, t.precio_regular, sugerido);
    if (!mejor || t.precio_actual < mejor.t.precio_actual || (t.precio_actual === mejor.t.precio_actual && o.pct > mejor.o.pct)) mejor = { t, o };
  }
  return mejor;
}

/* ---------- filtros ---------- */
/* Selector "Mostrar": Todos los juegos / Solo ofertas */
const mostrarSoloOfertas = () => document.querySelector('input[name="mostrar"]:checked')?.value === "ofertas";
function fijarMostrar(soloOfertas) {
  document.querySelector(`input[name="mostrar"][value="${soloOfertas ? "ofertas" : "todos"}"]`).checked = true;
}

function leerFiltros() {
  return {
    q: $("fBuscar").value.trim().toLocaleLowerCase("es"),
    tipo: $("fTipo").value, plataforma: $("fPlataforma").value,
    formato: $("fFormato").value, tienda: $("fTienda").value,
    orden: $("fOrden").value, oferta: mostrarSoloOfertas(),
  };
}

/* conDatos: valores que sí aparecen en el catálogo. Los demás (ej. "Físico", o una tienda caída como Epic)
 * se dejan en la lista con "(sin datos)": al elegirlos la página muestra "sin resultados", nunca un error. */
function llenarSelect(id, valores, etiqueta = (v) => v, conDatos = null) {
  const sel = $(id);
  const primero = sel.options[0];
  sel.replaceChildren(primero, ...valores.map((v) =>
    new Option(etiqueta(v) + (conDatos && !conDatos.has(v) ? " (sin datos)" : ""), v)));
}

function construirOpciones() {
  const v = estado.valores || {};
  const union = (base, extra) => [...new Set([...(base || []), ...extra])];
  // Plataforma: SOLO las que tienen al menos un precio (hoy PS4/PS5 no tienen tienda con precio, así que no aparecen;
  // si algún día hay precios de esas consolas, vuelven a aparecer solas).
  const platsConPrecio = new Set(estado.juegos.flatMap((j) => j.tiendas.map((t) => t.plataforma).filter(Boolean)));
  const plats = union(v.plataformas, estado.juegos.flatMap((j) => j.plataformas)).filter((p) => platsConPrecio.has(p));
  const tiendas = union(v.tiendas, estado.juegos.flatMap((j) => j.tiendas.map((t) => t.tienda)));
  const formatos = union(v.formatos, estado.juegos.flatMap((j) => j.tiendas.map((t) => t.formato).filter(Boolean)));
  const tipos = union(v.tipos_de_juego || ["digital", "fisico", "otro"], []);
  const hay = (lista) => new Set(lista);
  llenarSelect("fTipo", tipos, (x) => ETIQUETA_FORMATO[x] || x, hay(estado.juegos.map((j) => j.tipo)));
  llenarSelect("fPlataforma", plats);
  llenarSelect("fFormato", formatos, (x) => ETIQUETA_FORMATO[x] || x, hay(estado.juegos.flatMap((j) => j.tiendas.map((t) => t.formato))));
  llenarSelect("fTienda", tiendas, undefined, hay(estado.juegos.flatMap((j) => j.tiendas.map((t) => t.tienda))));
}

function aplicar() {
  const f = leerFiltros();
  const lista = [];
  for (const j of estado.juegos) {
    if (f.q && !j._nombre.includes(f.q)) continue;
    if (f.tipo && j.tipo !== f.tipo) continue;
    if (f.plataforma && !j.plataformas.includes(f.plataforma)) continue;
    const vis = ofertasVisibles(j, f);
    if ((f.tienda || f.formato) && vis.length === 0) continue;
    const mejor = mejorOferta(vis, j.sugerido);
    if (f.oferta && !(mejor && mejor.o.en)) continue;
    lista.push({ j, mejor });
  }

  const [campo, dir] = f.orden.split("-");
  const s = dir === "asc" ? 1 : -1;
  const clave = {
    nombre: (x) => x.j._nombre,
    precio: (x) => (x.mejor ? x.mejor.t.precio_actual : null),
    descuento: (x) => (x.mejor ? x.mejor.o.pct : null),
    metacritic: (x) => (x.j.meta ? x.j.meta.score : null),
  }[campo];
  lista.sort((a, b) => {
    const va = clave(a), vb = clave(b);
    if (va === null && vb === null) return a.j._nombre.localeCompare(b.j._nombre, "es");
    if (va === null) return 1; // los N/D siempre al final, en ambos sentidos
    if (vb === null) return -1;
    const c = typeof va === "string" ? va.localeCompare(vb, "es") : va - vb;
    return c !== 0 ? c * s : a.j._nombre.localeCompare(b.j._nombre, "es");
  });

  pintar(lista);
  guardarEnUrl(f);
}

/* ---------- tarjetas ---------- */
function imagenHtml(j, clase) {
  const lista = j._imagenes || [];
  const u = lista[0];
  const ini = esc((j.nombre || "?").split(/\s+/).slice(0, 2).map((p) => p[0]).join("").toUpperCase());
  const n = [...(j.nombre || "")].reduce((a, c) => (a + c.charCodeAt(0)) % 4, 0); // elige 1 de 4 degradados (styles.css: .ph.c0 … .ph.c3)
  const ph = `<div class="${clase} ph c${n}"><span>${ini}</span></div>`;
  if (!u) return ph;
  return `<img class="${clase}" src="${esc(u)}" alt="" loading="lazy" decoding="async" referrerpolicy="no-referrer" data-ph="${esc(ph)}" data-alt="${esc(JSON.stringify(lista.slice(1)))}" onerror="siguienteImagen(this)">`;
}

/* Si una imagen no carga, prueba la siguiente candidata; al final, las iniciales. */
function siguienteImagen(img) {
  let resto = [];
  try { resto = JSON.parse(img.dataset.alt || "[]"); } catch { resto = []; }
  if (resto.length) { img.dataset.alt = JSON.stringify(resto.slice(1)); img.src = resto[0]; }
  else img.outerHTML = img.dataset.ph;
}
window.siguienteImagen = siguienteImagen;

function metaHtml(meta) {
  if (!meta) return `<span class="mc nd" title="Sin dato de Metacritic">${ND}</span>`;
  const c = meta.score >= 75 ? "alto" : meta.score >= 50 ? "medio" : "bajo";
  return `<span class="mc ${c}" title="Metacritic${meta.plataforma ? " (" + esc(meta.plataforma) + ")" : ""}">${meta.score}</span>`;
}

function tarjeta({ j, mejor }, f = leerFiltros()) {
  const precio = mejor
    ? `<div class="precio">
         <span class="actual">${dinero(mejor.t.precio_actual)}</span>
         ${mejor.o.en ? `<s class="regular">${dinero(mejor.o.ref)}</s><span class="badge">−${Math.round(mejor.o.pct)}%</span>` : ""}
       </div>
       <div class="donde">en ${esc(mejor.t.tienda)}</div>`
    : `<div class="precio"><span class="actual nd">Sin precio${f.plataforma ? " para " + esc(f.plataforma) : ""}</span></div>` +
      `<div class="donde">${f.plataforma && j.tiendas.length ? "hay precio en otras plataformas" : ND}</div>`;
  return `<article class="card" tabindex="0" role="button" data-id="${esc(j.id)}" aria-label="Ver detalle de ${esc(j.nombre)}">
    <div class="img-wrap">${imagenHtml(j, "img")}${mejor && mejor.o.en ? `<span class="flag">OFERTA</span>` : ""}</div>
    <div class="info">
      <h2 class="titulo">${esc(j.nombre)}</h2>
      <div class="plats">${j.plataformas.map((p) => `<span class="chip">${esc(p)}</span>`).join("")}</div>
      <div class="fila">${precio ? `<div>${precio}</div>` : ""}${metaHtml(j.meta)}</div>
    </div>
  </article>`;
}

function pintar(lista, f = leerFiltros()) {
  $("conteo").textContent = mostrarSoloOfertas()
    ? `${lista.length} juegos en oferta (de ${estado.juegos.length})`
    : `${lista.length} de ${estado.juegos.length} juegos`;
  $("grid").innerHTML = lista.length
    ? lista.map((x) => tarjeta(x, f)).join("")
    : `<p class="vacio">Ningún juego cumple esos filtros. <button class="btn-sec" type="button" onclick="limpiar()">Limpiar filtros</button></p>`;
}

/* ---------- detalle ---------- */
function abrirDetalle(id, empujarHash = true) {
  const j = estado.juegos.find((x) => String(x.id) === String(id));
  if (!j) return;
  const filas = [...j.tiendas].sort((a, b) => a.precio_actual - b.precio_actual).map((t, i) => {
    const o = evaluarOferta(t.precio_actual, t.precio_regular, j.sugerido);
    const link = urlSegura(t.enlace);
    return `<tr class="${i === 0 ? "mejor" : ""}">
      <td>${esc(t.tienda)}${i === 0 ? ' <span class="tag">mejor</span>' : ""}</td>
      <td>${esc(t.plataforma || ND)}</td>
      <td>${esc(ETIQUETA_FORMATO[t.formato] || t.formato || ND)}</td>
      <td class="der"><strong>${dinero(t.precio_actual)}</strong></td>
      <td class="der">${t.precio_regular !== null ? (o.en ? `<s>${dinero(t.precio_regular)}</s>` : dinero(t.precio_regular)) : ND}</td>
      <td class="der">${o.en ? `−${o.pct.toFixed(0)}%` : "—"}</td>
      <td class="der">${link ? `<a href="${esc(link)}" target="_blank" rel="noopener noreferrer">Ir a la tienda ↗</a>` : ND}</td>
    </tr>${t.fecha_consulta ? `<tr class="sub"><td colspan="7">consultado ${esc(fecha(t.fecha_consulta))}</td></tr>` : ""}`;
  }).join("");

  const fuentes = Object.entries(j.estado_fuentes || {}).map(([k, v]) =>
    `<span class="fuente ${v === "ok" ? "ok" : "fallo"}" title="${v === "ok" ? "Consultada con éxito" : "Falló en la última corrida: se muestra el último dato bueno"}">${esc(k)}: ${v === "ok" ? "ok" : "falló"}</span>`).join("");

  $("detalleCuerpo").innerHTML = `
    <div class="d-cab">
      ${imagenHtml(j, "d-img")}
      <div>
        <h2 id="dTitulo">${esc(j.nombre)}</h2>
        <div class="plats">${j.plataformas.map((p) => `<span class="chip">${esc(p)}</span>`).join("")}</div>
        <dl class="datos">
          <div><dt>Precio sugerido</dt><dd>${dinero(j.sugerido)}</dd></div>
          <div><dt>Metacritic</dt><dd>${j.meta ? `${metaHtml(j.meta)} ${j.meta.plataforma ? `<small>(${esc(j.meta.plataforma)})</small>` : ""}` : ND}</dd></div>
          <div><dt>Tiempo para completarlo</dt><dd>${j.hltb !== null ? `${j.hltb.toFixed(1)} h` : ND}
            <small class="atrib">Fuente: <a href="${esc(urlSegura(j.hltb_enlace) || "https://howlongtobeat.com/")}" target="_blank" rel="noopener noreferrer">HowLongToBeat</a></small></dd></div>
          <div><dt>Lanzamiento</dt><dd>${/^\d{4}-\d{2}-\d{2}/.test(j.fecha_lanzamiento || "") ? esc(fecha(j.fecha_lanzamiento.slice(0, 10) + "T12:00:00Z", false)) : ND}</dd></div>
          <div><dt>Última actualización</dt><dd>${esc(fecha(j.ultima_actualizacion))}</dd></div>
        </dl>
      </div>
    </div>
    <h3>Precios por tienda <small>(de menor a mayor)</small></h3>
    ${filas ? `<div class="tabla-wrap"><table class="precios">
      <thead><tr><th>Tienda</th><th>Plataforma</th><th>Formato</th><th class="der">Precio</th><th class="der">Regular</th><th class="der">Desc.</th><th></th></tr></thead>
      <tbody>${filas}</tbody></table></div>` : `<p class="nd">No hay precios disponibles para este juego (${ND}).</p>`}
    ${fuentes ? `<h3>Estado de las fuentes</h3><div class="fuentes">${fuentes}</div>` : ""}`;
  const dlg = $("detalle");
  if (!dlg.open) dlg.showModal();
  if (empujarHash) history.replaceState(null, "", location.pathname + location.search + "#juego=" + encodeURIComponent(j.id));
}

function cerrarDetalle() {
  $("detalle").close();
}

/* ---------- URL (filtros compartibles y enlace directo a un juego) ---------- */
const CAMPOS_URL = { q: "fBuscar", tipo: "fTipo", plataforma: "fPlataforma", formato: "fFormato", tienda: "fTienda", orden: "fOrden" };
function guardarEnUrl(f) {
  const p = new URLSearchParams();
  for (const k of Object.keys(CAMPOS_URL)) if (f[k] && !(k === "orden" && f[k] === "descuento-desc")) p.set(k, k === "q" ? $("fBuscar").value.trim() : f[k]);
  if (f.oferta) p.set("oferta", "1");
  const qs = p.toString();
  history.replaceState(null, "", location.pathname + (qs ? "?" + qs : "") + location.hash);
}
function leerDeUrl() {
  const p = new URLSearchParams(location.search);
  for (const [k, id] of Object.entries(CAMPOS_URL)) {
    const v = p.get(k);
    if (v === null) continue;
    const el = $(id);
    if (el.tagName === "SELECT" && ![...el.options].some((o) => o.value === v)) continue;
    el.value = v;
  }
  fijarMostrar(p.get("oferta") === "1");
}

function limpiar() {
  $("fBuscar").value = ""; ["fTipo", "fPlataforma", "fFormato", "fTienda"].forEach((id) => ($(id).value = ""));
  $("fOrden").value = "descuento-desc"; fijarMostrar(false);
  aplicar();
}
window.limpiar = limpiar;

/* ---------- carga ---------- */
async function cargarJson(ruta) {
  const r = await fetch(ruta + "?v=" + Date.now(), { cache: "no-store" });
  if (!r.ok) throw new Error(`${ruta}: HTTP ${r.status}`);
  return r.json();
}

async function iniciar() {
  try {
    const [cat, valores, ejec] = await Promise.all([
      cargarJson("data/catalogo.json"),
      cargarJson("data/valores_validos.json").catch(() => null),
      cargarJson("data/ejecuciones.json").catch(() => []),
    ]);
    const lista = Array.isArray(cat) ? cat : cat.juegos || [];
    estado.juegos = lista.map(normalizar);
    estado.valores = valores;

    const generado = (!Array.isArray(cat) && cat.generado) ||
      lista.map((j) => j.ultima_actualizacion).filter(Boolean).sort().pop();
    $("metaDatos").innerHTML = `<strong>${estado.juegos.length}</strong> juegos · datos del ${esc(fecha(generado))} <small>(${esc(hace(generado))})</small>` +
      (cat.modo === "mock" ? ` <span class="aviso">DATOS DE EJEMPLO</span>` : "");

    const auto = (Array.isArray(ejec) ? ejec : []).find((e) => e.evento === "schedule" && e.resultado === "publicada");
    const ult = Array.isArray(ejec) ? ejec[0] : null;
    $("pieEjecucion").innerHTML = auto
      ? `Última actualización automática programada: ${esc(fecha(auto.fecha))} (${auto.juegos} juegos)` +
        (urlSegura(auto.run_url) ? ` · <a href="${esc(auto.run_url)}" target="_blank" rel="noopener">ver ejecución</a>` : "")
      : ult ? `Última ejecución: ${esc(fecha(ult.fecha))} (${esc(ult.evento)}, ${esc(ult.resultado)})` : "";

    construirOpciones();
    leerDeUrl();
    aplicar();
    const m = location.hash.match(/juego=([^&]+)/);
    if (m) abrirDetalle(decodeURIComponent(m[1]), false);
  } catch (e) {
    console.error(e);
    $("metaDatos").textContent = "No se pudieron cargar los datos.";
    $("grid").innerHTML = `<p class="vacio">No se pudo leer el catálogo. Intente recargar la página en unos minutos.</p>`;
  }
}

/* ---------- eventos ---------- */
["fTipo", "fPlataforma", "fFormato", "fTienda", "fOrden", "fMostrar"].forEach((id) => $(id).addEventListener("change", aplicar));
let tBuscar;
$("fBuscar").addEventListener("input", () => { clearTimeout(tBuscar); tBuscar = setTimeout(aplicar, 120); });
$("btnLimpiar").addEventListener("click", limpiar);
$("grid").addEventListener("click", (e) => { const c = e.target.closest(".card"); if (c) abrirDetalle(c.dataset.id); });
$("grid").addEventListener("keydown", (e) => {
  if (e.key !== "Enter" && e.key !== " ") return;
  const c = e.target.closest(".card"); if (c) { e.preventDefault(); abrirDetalle(c.dataset.id); }
});
$("btnCerrar").addEventListener("click", cerrarDetalle);
$("detalle").addEventListener("click", (e) => { if (e.target === $("detalle")) cerrarDetalle(); });
$("detalle").addEventListener("close", () => history.replaceState(null, "", location.pathname + location.search));

iniciar();
