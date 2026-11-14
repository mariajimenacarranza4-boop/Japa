using System.Text;

namespace ScraperJuegos;

/// <summary>Ediciones puntuales del CSV del REPO (conservan BOM, saltos de línea y comillas).</summary>
public static class EditorRepoCsv
{
    /// <summary>Cambia la fecha_lanzamiento de las filas cuyo id está en la lista (aaaa-mm-dd). Devuelve cuántas filas cambió.</summary>
    public static int ActualizarFechas(string destino, IReadOnlyDictionary<int, string> nuevas)
    {
        var primeros = new byte[3];
        using (var fsb = File.OpenRead(destino)) { _ = fsb.Read(primeros, 0, 3); }
        bool bom = primeros[0] == 0xEF && primeros[1] == 0xBB && primeros[2] == 0xBF;
        string texto = File.ReadAllText(destino, Encoding.UTF8);
        string fin = texto.Contains("\r\n") ? "\r\n" : "\n";
        var lineas = texto.TrimStart('\uFEFF').Replace("\r\n", "\n").Split('\n').ToList();
        var enc = RepoCsv.DividirLinea(lineas[0]).Select(h => h.Trim().ToLowerInvariant()).ToList();
        int cI = enc.IndexOf("id"), cF = enc.IndexOf("fecha_lanzamiento");
        if (cI < 0 || cF < 0) return 0;
        static string Q(string v) => v.Contains(';') || v.Contains('"') ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
        int n = 0;
        for (int i = 1; i < lineas.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(lineas[i])) continue;
            var c = RepoCsv.DividirLinea(lineas[i]);
            if (c.Count <= Math.Max(cI, cF) || !int.TryParse(c[cI].Trim(), out int id) || !nuevas.TryGetValue(id, out var f)) continue;
            c[cF] = f; lineas[i] = string.Join(";", c.Select(Q)); n++;
        }
        File.WriteAllText(destino, (bom ? "\uFEFF" : "") + string.Join(fin, lineas), new UTF8Encoding(false));
        return n;
    }

    /// <summary>Vacía el hltb_id de las filas cuyo (nombre, id) está en la lista. Devuelve cuántas filas cambió.</summary>
    public static int QuitarHltbIds(string destino, IReadOnlyCollection<(string nombre, string id)> malos)
    {
        var primeros = new byte[3];
        using (var fsb = File.OpenRead(destino)) { _ = fsb.Read(primeros, 0, 3); }
        bool bom = primeros[0] == 0xEF && primeros[1] == 0xBB && primeros[2] == 0xBF;
        string texto = File.ReadAllText(destino, Encoding.UTF8);
        string fin = texto.Contains("\r\n") ? "\r\n" : "\n";
        var lineas = texto.TrimStart('\uFEFF').Replace("\r\n", "\n").Split('\n').ToList();
        var enc = RepoCsv.DividirLinea(lineas[0]).Select(h => h.Trim().ToLowerInvariant()).ToList();
        int cN = enc.IndexOf("nombre"), cH = enc.IndexOf("hltb_id");
        if (cN < 0 || cH < 0) return 0;
        static string Q(string v) => v.Contains(';') || v.Contains('"') ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
        int n = 0;
        for (int i = 1; i < lineas.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(lineas[i])) continue;
            var c = RepoCsv.DividirLinea(lineas[i]);
            if (c.Count <= Math.Max(cN, cH)) continue;
            if (!malos.Any(m => m.nombre.Equals(c[cN].Trim(), StringComparison.OrdinalIgnoreCase) && m.id == c[cH].Trim())) continue;
            c[cH] = ""; lineas[i] = string.Join(";", c.Select(Q)); n++;
        }
        File.WriteAllText(destino, (bom ? "\uFEFF" : "") + string.Join(fin, lineas), new UTF8Encoding(false));
        return n;
    }
}
