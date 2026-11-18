using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ScraperJuegos;

/// <summary>
/// Estrategia de coincidencia de nombres (el mismo juego se llama distinto en cada sitio):
///  1. Se NORMALIZA el nombre: sin tildes, minúsculas, sin ™ ® ©, "&amp;"→"and", sin puntuación,
///     "the" inicial fuera, números romanos → dígitos (VII = 7), y se quitan sufijos de edición
///     ("Standard/Deluxe/Ultimate/Complete/Gold Edition", "GOTY", "Director's Cut", "Remastered"...).
///  2. Hay coincidencia SOLO si los nombres normalizados son IGUALES.
///     Así "Doom" ≠ "Doom Eternal", "God of War" ≠ "God of War Ragnarök", "FIFA 23" ≠ "FIFA 22".
///  3. Además de la plataforma (que verifica cada scraper), si hay duda NO se adivina: no_encontrado.
///  4. Si un nombre legítimo no coincide (ej. "Marvel's Spider-Man" vs "Marvel's Spider-Man Remastered" en PC),
///     se corrige en el REPO con la columna nombre_busqueda, que también cuenta como nombre aceptado.
/// </summary>
public static class NormalizadorNombres
{
    private static readonly Dictionary<string, string> Romanos = new()
    {
        ["ii"] = "2", ["iii"] = "3", ["iv"] = "4", ["v"] = "5", ["vi"] = "6", ["vii"] = "7", ["viii"] = "8",
        ["ix"] = "9", ["x"] = "10", ["xi"] = "11", ["xii"] = "12", ["xiii"] = "13", ["xiv"] = "14", ["xv"] = "15",
        ["xvi"] = "16", ["xvii"] = "17", ["xviii"] = "18", ["xix"] = "19", ["xx"] = "20"
    };

    // Se quitan solo al FINAL del nombre (ya normalizado), y se repite por si hay varios.
    private static readonly string[] SufijosEdicion =
    {
        // Algunas fuentes (ej. claves de Eneba) agregan la plataforma al título ("ELDEN RING PS4 & PS5"): la etiqueta
        // de plataforma NO es parte del nombre (la plataforma se verifica aparte). Los más largos van primero.
        "ps4 and ps5", "ps5 and ps4", "ps4 ps5", "ps5", "ps4",
        "digital deluxe edition", "deluxe edition", "standard edition", "ultimate edition", "complete edition",
        "gold edition", "premium edition", "game of the year edition", "goty edition", "goty",
        "directors cut", "director s cut", "definitive edition", "special edition", "remastered", "deluxe", "edition"
    };

    public static string Normalizar(string? nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre)) return "";
        string d = nombre.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (char c in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (c is '™' or '®' or '©') continue;
            sb.Append(c == '&' ? " and " : char.ToLowerInvariant(c));
        }
        string t = Regex.Replace(sb.ToString(), "[^a-z0-9]+", " ").Trim();
        t = Regex.Replace(t, "^the ", "");

        var tokens = t.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                      .Select(tok => Romanos.TryGetValue(tok, out var n) ? n : tok).ToList();
        t = string.Join(' ', tokens);

        bool cambio;
        do
        {
            cambio = false;
            foreach (var suf in SufijosEdicion)
            {
                if (t.EndsWith(" " + suf, StringComparison.Ordinal))
                { t = t[..^(suf.Length + 1)].TrimEnd(); cambio = true; break; }
            }
        } while (cambio);
        return t;
    }

    public static bool Coincide(string? a, string? b)
    {
        string na = Normalizar(a), nb = Normalizar(b);
        return na.Length > 0 && na == nb;
    }

    /// <summary>¿El nombre de la fuente corresponde a este juego? (nombre del REPO o su nombre_busqueda)</summary>
    public static bool CoincideConJuego(Juego juego, string? nombreFuente) =>
        Coincide(juego.Nombre, nombreFuente) || Coincide(juego.NombreBusqueda, nombreFuente);

    /// <summary>Slug estilo Metacritic: "Marvel's Spider-Man 2" -> "marvels-spider-man-2".</summary>
    public static string Slug(string nombre)
    {
        string d = nombre.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (char c in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (c is '\'' or '’' or '™' or '®' or '©') continue;
            sb.Append(char.ToLowerInvariant(c));
        }
        return Regex.Replace(sb.ToString(), "[^a-z0-9]+", "-").Trim('-');
    }
}
