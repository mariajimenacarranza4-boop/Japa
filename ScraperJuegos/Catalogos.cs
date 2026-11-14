namespace ScraperJuegos;

/// <summary>
/// Valores válidos que usa todo el equipo. C los usa para armar los filtros
/// (se exportan con: dotnet run -- catalogos). Si se agrega una tienda o plataforma, se agrega aquí.
/// </summary>
public static class Catalogos
{
    public const string Digital = "digital";
    public const string Fisico = "fisico";

    public static readonly string[] Plataformas = { "PS5", "PS4", "Xbox Series", "Xbox One", "PC", "Switch" };
    public static readonly string[] Formatos = { Digital, Fisico };
    public static readonly string[] Tiendas = { "Steam", "Epic Games Store", "Nintendo eShop", "Eneba" };

    /// <summary>Acepta variantes comunes del CSV ("ps5", "PlayStation 5", "xbox series x"...).</summary>
    public static string NormalizarPlataforma(string texto)
    {
        string t = (texto ?? "").Trim().ToLowerInvariant();
        return t switch
        {
            "ps5" or "playstation 5" or "playstation5" => "PS5",
            "ps4" or "playstation 4" or "playstation4" => "PS4",
            "xbox series" or "xbox series x" or "xbox series s" or "xbox series x/s" or "xbox series x|s" => "Xbox Series",
            "xbox one" or "xboxone" => "Xbox One",
            "pc" or "windows" or "steam" => "PC",
            "switch" or "nintendo switch" => "Switch",
            _ => (texto ?? "").Trim()
        };
    }
}
