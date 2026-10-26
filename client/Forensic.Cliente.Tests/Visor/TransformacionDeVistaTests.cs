using Forensic.Cliente.Nucleo.Visor;

namespace Forensic.Cliente.Tests.Visor;

/// <summary>
/// Lo que viaja al servidor son píxeles de la imagen original, no de la pantalla. Convención: el
/// píxel i ocupa [i, i+1) en coordenadas de imagen.
/// </summary>
public class TransformacionDeVistaTests
{
    private static void Cerca(Punto esperado, Punto real) =>
        Assert.True(Math.Abs(esperado.X - real.X) < 1e-9 && Math.Abs(esperado.Y - real.Y) < 1e-9, $"esperado {esperado}, real {real}");

    [Fact]
    public void Sin_zoom_ni_desplazamiento_pantalla_e_imagen_coinciden()
    {
        var t = new TransformacionDeVista();

        Cerca(new Punto(12.5, 7), t.PantallaAImagen(new Punto(12.5, 7)));
    }

    [Theory]
    [InlineData(2.0, 30, 40)]
    [InlineData(0.37, -120, 15.5)]
    [InlineData(8.0, 0, -300)]
    public void Pantalla_imagen_pantalla_es_la_identidad_con_cualquier_zoom_y_desplazamiento(double zoom, double dx, double dy)
    {
        var t = new TransformacionDeVista(zoom, new Punto(dx, dy));
        var pantalla = new Punto(123.25, 456.75);

        Cerca(pantalla, t.ImagenAPantalla(t.PantallaAImagen(pantalla)));
        Cerca(new Punto((123.25 - dx) / zoom, (456.75 - dy) / zoom), t.PantallaAImagen(pantalla));
    }

    [Fact]
    public void El_zoom_con_la_rueda_deja_quieto_el_punto_de_la_imagen_bajo_el_cursor()
    {
        var t = new TransformacionDeVista(1.5, new Punto(20, 10));
        var cursor = new Punto(300, 200);
        var antes = t.PantallaAImagen(cursor);

        var despues = t.ZoomEn(2.0, cursor);

        Assert.Equal(3.0, despues.Zoom, 9);
        Cerca(antes, despues.PantallaAImagen(cursor));
    }

    [Fact]
    public void El_zoom_queda_acotado()
    {
        var t = new TransformacionDeVista();

        Assert.Equal(TransformacionDeVista.ZoomMaximo, t.ZoomEn(1e6, new Punto(0, 0)).Zoom);
        Assert.Equal(TransformacionDeVista.ZoomMinimo, t.ZoomEn(1e-6, new Punto(0, 0)).Zoom);
    }

    [Fact]
    public void Ajustar_encaja_la_imagen_completa_centrada_en_la_vista()
    {
        var t = TransformacionDeVista.Ajustada(anchoImagen: 400, altoImagen: 200, anchoVista: 800, altoVista: 800);

        Assert.Equal(2.0, t.Zoom, 9);
        Cerca(new Punto(0, 200), t.Desplazamiento); // 400 px de alto en una vista de 800: 200 de margen arriba
        Cerca(new Punto(400, 200), t.ImagenAPantalla(new Punto(200, 0)));
    }

    [Fact]
    public void Desplazar_mueve_la_imagen_en_pixeles_de_pantalla()
    {
        var t = new TransformacionDeVista(2.0, new Punto(0, 0)).Desplazada(10, -4);

        Cerca(new Punto(10, -4), t.Desplazamiento);
        Cerca(new Punto(0, 0), t.PantallaAImagen(new Punto(10, -4)));
    }

    [Fact]
    public void Un_roi_arrastrado_en_pantalla_da_enteros_en_pixeles_de_la_imagen()
    {
        // Zoom 2 y la imagen corrida 100,50: el píxel (x,y) cubre la pantalla [100+2x, 102+2x).
        var t = new TransformacionDeVista(2.0, new Punto(100, 50));

        var roi = t.RegionEnImagen(new Punto(111, 63), new Punto(140.5, 80), anchoImagen: 64, altoImagen: 48);

        // Imagen: de (5.5, 6.5) a (20.25, 15) → origen hacia abajo, extremo hacia arriba: incluye todo lo tocado.
        Assert.Equal(new Region(5, 6, 16, 9), roi);
    }

    [Fact]
    public void Arrastrar_en_cualquier_sentido_da_la_misma_region()
    {
        var t = new TransformacionDeVista(2.0, new Punto(100, 50));

        Assert.Equal(
            t.RegionEnImagen(new Punto(111, 63), new Punto(140.5, 80), 64, 48),
            t.RegionEnImagen(new Punto(140.5, 80), new Punto(111, 63), 64, 48));
    }

    [Fact]
    public void Un_roi_que_se_sale_de_la_imagen_se_recorta_a_sus_bordes()
    {
        var t = new TransformacionDeVista(1.0, new Punto(0, 0));

        Assert.Equal(new Region(0, 0, 64, 48), t.RegionEnImagen(new Punto(-30, -30), new Punto(500, 500), 64, 48));
        Assert.Equal(new Region(60, 40, 4, 8), t.RegionEnImagen(new Punto(60.2, 40.9), new Punto(90, 90), 64, 48));
    }

    [Fact]
    public void Un_roi_completamente_fuera_de_la_imagen_no_es_una_region()
    {
        var t = new TransformacionDeVista(1.0, new Punto(0, 0));

        Assert.Null(t.RegionEnImagen(new Punto(70, 10), new Punto(90, 20), 64, 48));
    }

    [Fact]
    public void Un_clic_sin_arrastre_marca_un_solo_pixel()
    {
        var t = new TransformacionDeVista(4.0, new Punto(0, 0));

        Assert.Equal(new Region(2, 3, 1, 1), t.RegionEnImagen(new Punto(9, 13), new Punto(9, 13), 64, 48));
    }

    [Theory]
    [InlineData(0.0, 0.0, 0, 0)]
    [InlineData(7.99, 3.01, 1, 0)]
    [InlineData(-5, 500, 0, 47)]
    public void Un_punto_de_pantalla_cae_en_el_pixel_de_la_imagen_que_lo_contiene(double x, double y, int px, int py)
    {
        var t = new TransformacionDeVista(4.0, new Punto(0, 0));

        Assert.Equal((px, py), t.PixelEnImagen(new Punto(x, y), 64, 48));
    }
}
