namespace ScraperJuegos;

/// <summary>Resultado de evaluar una oferta. Referencia: "precio_regular", "precio_sugerido" o "ninguna".</summary>
public record Oferta(bool EnOferta, decimal DescuentoPct, string Referencia);

/// <summary>Resumen por juego que A copia al registro final (Contrato 2).</summary>
public record Consolidado(decimal? MejorPrecio, string? MejorTienda, decimal DescuentoPct, bool EnOferta,
                          decimal? PrecioRegularMejor);

/// <summary>
/// CRITERIO DEL INDICADOR DE OFERTA (lo define B, lo aplica A, lo muestra C):
///
///  1) Criterio principal (por tienda): EN OFERTA si precio_actual &lt; precio_regular de esa tienda.
///        descuento % = (precio_regular − precio_actual) / precio_regular × 100
///  2) Apoyo: si la tienda NO informa precio_regular, se compara con el precio_sugerido del REPO
///        (solo si precio_actual &lt; precio_sugerido) con la misma fórmula.
///  3) Si ninguna condición se cumple: no está en oferta y el descuento es 0.
///
///  Ejemplo: regular 59.99, actual 29.99 -> (59.99−29.99)/59.99×100 = 50.01 % -> en oferta.
///  Todos los precios están en USD (Steam cc=us, Epic y Nintendo región US, Eneba solo USD) para que sean comparables
///  con el precio_sugerido del REPO. Tolerancia de 1 centavo para evitar falsos positivos por redondeo.
/// </summary>
public static class CalculadorOferta
{
    private const decimal Tolerancia = 0.01m;

    public static Oferta Evaluar(decimal precioActual, decimal? precioRegular, decimal precioSugerido)
    {
        if (precioRegular is > 0)
        {
            if (precioActual < precioRegular.Value - Tolerancia)
                return new Oferta(true, Pct(precioRegular.Value, precioActual), "precio_regular");
            return new Oferta(false, 0m, "ninguna");
        }
        if (precioSugerido > 0 && precioActual < precioSugerido - Tolerancia)
            return new Oferta(true, Pct(precioSugerido, precioActual), "precio_sugerido");
        return new Oferta(false, 0m, "ninguna");
    }

    private static decimal Pct(decimal referencia, decimal actual) =>
        Math.Round((referencia - actual) / referencia * 100m, 2);

    /// <summary>
    /// Elige la mejor tienda: menor precio_actual (empate: mayor descuento). El descuento y el precio tachado
    /// que se muestran son los de ESA tienda, así la tarjeta es coherente con el cálculo.
    /// Ignora tiendas con ok:false o con moneda distinta de USD.
    /// </summary>
    public static Consolidado Consolidar(IEnumerable<ResultadoTienda> tiendas, decimal precioSugerido)
    {
        // Solo USD (o sin moneda indicada): mezclar monedas daría un "mejor precio" falso.
        var buenas = tiendas.Where(t => t.Ok && t.PrecioActual.HasValue
                                        && (string.IsNullOrEmpty(t.Moneda) || t.Moneda.Equals("USD", StringComparison.OrdinalIgnoreCase))).ToList();
        if (buenas.Count == 0) return new Consolidado(null, null, 0m, false, null);

        var candidatas = buenas
            .Select(t => (t, o: Evaluar(t.PrecioActual!.Value, t.PrecioRegular, precioSugerido)))
            .OrderBy(x => x.t.PrecioActual).ThenByDescending(x => x.o.DescuentoPct).ToList();
        var mejor = candidatas[0];
        // en_oferta y descuento_pct describen la MISMA tienda (la del mejor precio): lo que se muestra es coherente.
        return new Consolidado(mejor.t.PrecioActual, mejor.t.Tienda, mejor.o.DescuentoPct,
                               mejor.o.EnOferta, mejor.t.PrecioRegular);
    }
}
