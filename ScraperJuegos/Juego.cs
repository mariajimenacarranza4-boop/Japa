namespace ScraperJuegos;

/// <summary>
/// Una fila del REPO: un juego en UNA plataforma.
/// (Elden Ring en PS5 y Elden Ring en PC son dos Juego distintos,
/// porque Metacritic y las tiendas dan datos distintos por plataforma.)
///
/// Los 5 primeros campos son los obligatorios del enunciado. Los demás son
/// extras OPCIONALES del REPO (se pueden dejar vacíos en el CSV):
///   ImagenUrl       - imagen de respaldo si ninguna tienda la entrega.
///   SteamAppId      - evita la búsqueda por nombre en Steam.
///   NombreBusqueda  - nombre exacto en las fuentes cuando difiere del nombre del REPO.
///   MetacriticSlug  - parte de la URL de Metacritic (ej. "elden-ring").
///   HltbId          - id numérico de HowLongToBeat (evita su búsqueda).
/// </summary>
public record Juego(
    int Id,
    string Nombre,
    string Plataforma,
    DateTime FechaLanzamiento,
    decimal PrecioSugerido,
    string? ImagenUrl = null,
    string? SteamAppId = null,
    string? NombreBusqueda = null,
    string? MetacriticSlug = null,
    string? HltbId = null)
{
    /// <summary>Nombre que se manda a los buscadores de las fuentes.</summary>
    public string NombreParaBuscar =>
        string.IsNullOrWhiteSpace(NombreBusqueda) ? Nombre : NombreBusqueda!;
}

/// <summary>Un juego del REPO con todas sus plataformas (para el Nivel 1 de A: "un registro por juego").</summary>
public record JuegoAgrupado(string Nombre, IReadOnlyList<Juego> Filas)
{
    public IEnumerable<string> Plataformas => Filas.Select(f => f.Plataforma);
}
