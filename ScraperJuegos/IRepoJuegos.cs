namespace ScraperJuegos;

/// <summary>Resultado de leer el REPO: los juegos válidos y los errores encontrados.</summary>
public record ResultadoCarga(List<Juego> Juegos, List<string> Errores)
{
    /// <summary>Agrupa las filas (juego+plataforma) por juego. 200+ "juegos" del enunciado = grupos distintos.</summary>
    public List<JuegoAgrupado> AgruparPorJuego() =>
        Juegos.GroupBy(j => j.Nombre, StringComparer.OrdinalIgnoreCase)
              .Select(g => new JuegoAgrupado(g.First().Nombre, g.ToList()))
              .ToList();
}

/// <summary>
/// Contrato del REPO. A (orquestador) solo conoce esta interfaz,
/// así que hoy leemos de un CSV y mañana se puede cambiar a una base
/// de datos en la nube sin que A tenga que modificar nada.
/// </summary>
public interface IRepoJuegos
{
    ResultadoCarga Cargar();
}
