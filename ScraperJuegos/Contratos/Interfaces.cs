namespace ScraperJuegos;

/// <summary>
/// Scraper de una tienda (Contrato 1). Reglas que A puede dar por hechas:
///  - NUNCA lanza excepciones (salvo cancelación pedida por A): todo error vuelve como ResultadoTienda.Fallo.
///  - SIN estado compartido: cada llamada es independiente, se puede ejecutar en paralelo (Nivel 3).
///  - Respeta el límite de concurrencia/pausa del ILimitadorSolicitudes que se le inyecta.
/// </summary>
public interface ITiendaScraper
{
    string Tienda { get; }
    string Dominio { get; }
    bool SoportaPlataforma(string plataforma);
    Task<ResultadoTienda> ConsultarAsync(Juego juego, CancellationToken ct = default);
}

public interface IMetacriticScraper
{
    Task<ResultadoMetacritic> ConsultarAsync(Juego juego, CancellationToken ct = default);
}

public interface IHltbScraper
{
    Task<ResultadoHltb> ConsultarAsync(Juego juego, CancellationToken ct = default);
}
