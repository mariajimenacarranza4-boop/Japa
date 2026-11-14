using ScraperJuegos;

// Versión de referencia: mismo resultado, cero paralelismo (base del speedup).
public static class Secuencial
{
    public static async Task<List<RegistroJuego>> Ejecutar(List<JuegoAgrupado> juegos, Fuentes f)
    {
        var resultados = new List<RegistroJuego>();
        foreach (var g in juegos)
            resultados.Add(await Paralelo.ProcesarJuego(g, f, nivel2: false, nivel3: false));
        return resultados;
    }
}
