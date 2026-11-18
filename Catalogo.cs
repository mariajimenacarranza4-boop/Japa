using System.Text.Json;
using ScraperJuegos;

// Consistencia de datos (rubro "Robustez"):
//  - Si una fuente falla, NO se sobrescribe con vacío: se conserva el último dato bueno
//    (cada dato lleva su propia fecha) y la fuente queda marcada "fallo" en estado_fuentes.
//  - El catálogo se guarda en un archivo temporal y luego se renombra: si el programa se
//    cae a mitad de escritura, el catálogo anterior queda intacto.
public static class Catalogo
{
    // Llama a esto UNA vez por corrida (desde el hilo principal, después de Paralelo.Ejecutar).
    // idsVigentes != null (corrida completa del REPO): los juegos del catálogo que ya no están en el REPO se eliminan.
    public static List<RegistroJuego> Actualizar(string ruta, List<RegistroJuego> nuevos, ISet<string>? idsVigentes = null)
    {
        var previos = Cargar(ruta);
        int eliminados = 0;
        if (idsVigentes != null)
            foreach (var id in previos.Keys.Where(k => !idsVigentes.Contains(k)).ToList())
            {
                previos.Remove(id);
                eliminados++;
            }
        int conservados = 0;
        var finales = new List<RegistroJuego>();

        foreach (var n in nuevos)
        {
            previos.TryGetValue(n.Id, out var p);
            var (r, c) = Fusionar(n, p);
            finales.Add(r);
            conservados += c;
            previos[n.Id] = r;   // los juegos que no se corrieron ahora se mantienen tal cual
        }

        Guardar(ruta, previos.Values
            .OrderBy(x => int.TryParse(x.Id, out var i) ? i : int.MaxValue).ToList());

        Console.WriteLine($"Catálogo: {finales.Count} juegos actualizados, {previos.Count} en total, " +
                          $"{conservados} datos anteriores conservados por fallos" +
                          (eliminados > 0 ? $", {eliminados} juegos quitados del REPO eliminados." : "."));
        return finales;
    }

    public static (RegistroJuego, int) Fusionar(RegistroJuego n, RegistroJuego? p)
    {
        if (p == null) return (n, 0);
        int conservados = 0;

        // Tiendas: n.Tiendas solo trae las que respondieron ok ahora. Las que faltan se
        // reponen con el dato anterior (conserva su propia fecha_consulta).
        var tiendas = n.Tiendas.ToList();
        foreach (var t in p.Tiendas)
            if (!tiendas.Any(x => x.Tienda == t.Tienda && x.Plataforma == t.Plataforma)
                && !EsDatoSospechoso(t, n.PrecioSugerido))   // un dato viejo implausible no se repone "por conservar"
            {
                tiendas.Add(t);
                conservados++;
            }
        n.Tiendas = tiendas;

        // Se recalcula mejor precio / oferta con los datos ya fusionados (mismo criterio de B).
        var c = CalculadorOferta.Consolidar(tiendas, n.PrecioSugerido);
        n.MejorPrecio = c.MejorPrecio;
        n.MejorTienda = c.MejorTienda;
        n.PrecioRegularMejor = c.PrecioRegularMejor;
        n.DescuentoPct = c.DescuentoPct;
        n.EnOferta = c.EnOferta;

        // Si TODAS las fuentes fallaron ahora, no hay nada nuevo: la fecha global se queda con la anterior
        // (así el sitio no aparenta datos frescos; cada dato además conserva su propia fecha).
        if (n.EstadoFuentes.Count > 0 && n.EstadoFuentes.Values.All(v => v == "fallo"))
            n.UltimaActualizacion = p.UltimaActualizacion;

        // Metacritic, por plataforma
        foreach (var m in p.MetacriticPorPlataforma)
            if (!n.MetacriticPorPlataforma.Any(x => x.Plataforma == m.Plataforma))
            {
                n.MetacriticPorPlataforma.Add(m);
                conservados++;
            }
        n.Metacritic = n.MetacriticPorPlataforma.OrderByDescending(m => m.Score).FirstOrDefault();

        // HLTB
        if (n.HltbHoras == null && p.HltbHoras != null)
        {
            n.HltbHoras = p.HltbHoras;
            n.HltbFecha = p.HltbFecha;
            conservados++;
        }
        return (n, conservados);
    }

    // Un precio de Eneba absurdamente bajo frente al sugerido del REPO (criterio de B: EnebaScraper.PrecioSospechoso).
    private static bool EsDatoSospechoso(ResultadoTienda t, decimal precioSugerido) =>
        t.Tienda == "Eneba" && t.PrecioActual is decimal p && EnebaScraper.PrecioSospechoso(p, precioSugerido);

    /// <summary>
    /// Limpieza de un catálogo ya generado (comando "limpiar"): quita los precios sospechosos de Eneba, recalcula mejor precio /
    /// descuento / oferta con el criterio de B y ajusta estado_fuentes (una fuente sin ningún dato bueno queda en "fallo";
    /// la clave de una tienda que ya no existe en los datos, como Epic apagada, se elimina). Devuelve cuántos precios quitó.
    /// </summary>
    public static int Limpiar(string ruta)
    {
        var todos = Cargar(ruta);
        int quitados = 0;
        foreach (var r in todos.Values)
        {
            int antes = r.Tiendas.Count;
            r.Tiendas = r.Tiendas.Where(t => !EsDatoSospechoso(t, r.PrecioSugerido)).ToList();
            int q = antes - r.Tiendas.Count;
            if (q > 0)
            {
                quitados += q;
                var c = CalculadorOferta.Consolidar(r.Tiendas, r.PrecioSugerido);
                r.MejorPrecio = c.MejorPrecio; r.MejorTienda = c.MejorTienda; r.PrecioRegularMejor = c.PrecioRegularMejor;
                r.DescuentoPct = c.DescuentoPct; r.EnOferta = c.EnOferta;
                if (!r.Tiendas.Any(t => t.Tienda == "Eneba")) r.EstadoFuentes["Eneba"] = "fallo";
            }
            foreach (var k in r.EstadoFuentes.Keys.ToList())
                if (k != "metacritic" && k != "hltb" && !r.Tiendas.Any(t => t.Tienda == k) && r.EstadoFuentes[k] == "fallo"
                    && k == "Epic Games Store")
                    r.EstadoFuentes.Remove(k);
        }
        Guardar(ruta, todos.Values.OrderBy(x => int.TryParse(x.Id, out var i) ? i : int.MaxValue).ToList());
        Console.WriteLine($"Catálogo limpiado: {quitados} precio(s) sospechoso(s) de Eneba quitados en {todos.Count} juegos.");
        return quitados;
    }

    private static Dictionary<string, RegistroJuego> Cargar(string ruta)
    {
        try
        {
            if (!File.Exists(ruta)) return new();
            var lista = JsonSerializer.Deserialize<List<RegistroJuego>>(
                File.ReadAllText(ruta), Serializacion.Opciones) ?? new();
            return lista.GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.Last());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Aviso: no se pudo leer {ruta} ({ex.Message}). Se guarda copia en .corrupto y se parte de cero.");
            try { File.Copy(ruta, ruta + ".corrupto", true); } catch { }
            return new();
        }
    }

    private static void Guardar(string ruta, List<RegistroJuego> lista)
    {
        string tmp = ruta + ".tmp";
        File.WriteAllText(tmp, Serializacion.AJson(lista));
        File.Move(tmp, ruta, overwrite: true);
    }
}
