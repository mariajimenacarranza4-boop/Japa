using ScraperJuegos;

// CONTRATO 2: registro final por juego (A escribe, C lee). Solo se AGREGAN campos, nunca se quitan.

public record MetacriticInfo(int Score, string Plataforma, DateTimeOffset? Fecha = null);

public class RegistroJuego
{
    public string Id { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string ImagenUrl { get; set; } = "";
    public List<string> Plataformas { get; set; } = new();
    public DateTime FechaLanzamiento { get; set; }
    public decimal PrecioSugerido { get; set; }
    public List<ResultadoTienda> Tiendas { get; set; } = new();   // solo las que respondieron ok
    public decimal? MejorPrecio { get; set; }
    public string? MejorTienda { get; set; }
    public decimal? PrecioRegularMejor { get; set; }              // AGREGADO: precio tachado de la mejor tienda
    public decimal DescuentoPct { get; set; }
    public bool EnOferta { get; set; }
    public MetacriticInfo? Metacritic { get; set; }               // el mayor entre las plataformas del REPO
    public List<MetacriticInfo> MetacriticPorPlataforma { get; set; } = new();  // AGREGADO
    public double? HltbHoras { get; set; }
    
    public DateTimeOffset? HltbFecha { get; set; }
    public DateTimeOffset UltimaActualizacion { get; set; }
    public Dictionary<string, string> EstadoFuentes { get; set; } = new();
}
