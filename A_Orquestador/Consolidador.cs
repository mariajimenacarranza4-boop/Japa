using ScraperJuegos;

public static class Consolidador
{
    // Un registro por juego (con todas sus plataformas). El criterio de oferta es el OFICIAL de B.
    public static RegistroJuego Armar(JuegoAgrupado g, List<ResultadoTienda> tiendas,
                                      List<ResultadoMetacritic> mcs, ResultadoHltb hltb)
    {
        var primera = g.Filas[0];
        var buenas = tiendas.Where(t => t.Ok && t.PrecioActual != null).ToList();
        var okMc = mcs.Where(m => m.Ok && m.Score != null).ToList();
        var c = CalculadorOferta.Consolidar(buenas, primera.PrecioSugerido);

        var r = new RegistroJuego
        {
            Id = primera.Id.ToString(),
            Nombre = g.Nombre,
            ImagenUrl = g.Filas.Select(f => f.ImagenUrl).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u))
                        ?? buenas.Select(t => t.ImagenUrl).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u))
                        ?? "",
            Plataformas = g.Plataformas.ToList(),
            FechaLanzamiento = g.Filas.Min(f => f.FechaLanzamiento),
            PrecioSugerido = primera.PrecioSugerido,
            Tiendas = buenas,
            MejorPrecio = c.MejorPrecio,
            MejorTienda = c.MejorTienda,
            PrecioRegularMejor = c.PrecioRegularMejor,
            DescuentoPct = c.DescuentoPct,
            EnOferta = c.EnOferta,
            MetacriticPorPlataforma = okMc.Select(m => new MetacriticInfo(m.Score!.Value, m.Plataforma ?? "", m.FechaConsulta)).ToList(),
            HltbHoras = hltb.Ok ? hltb.HorasCompleto : null,
            HltbFecha = hltb.Ok ? hltb.FechaConsulta : null,
            UltimaActualizacion = DateTimeOffset.UtcNow
        };
        r.Metacritic = r.MetacriticPorPlataforma.OrderByDescending(m => m.Score).FirstOrDefault();

        foreach (var grupo in tiendas.GroupBy(t => t.Tienda))
            r.EstadoFuentes[grupo.Key] = grupo.Any(t => t.Ok) ? "ok" : "fallo";
        r.EstadoFuentes["metacritic"] = okMc.Count > 0 ? "ok" : "fallo";
        r.EstadoFuentes["hltb"] = hltb.Ok ? "ok" : "fallo";
        return r;
    }
}
