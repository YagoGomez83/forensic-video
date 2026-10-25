using Forensic.Domain.Comun;
using Forensic.Domain.Identidad;

namespace Forensic.Domain.Tests;

public class PoliticaDeContrasenaTests
{
    [Theory]
    [InlineData("corta")]
    [InlineData("once-chars!")]
    [InlineData("            ")]
    public void Rechaza_contrasenas_de_menos_de_doce_caracteres_o_en_blanco(string contrasena)
    {
        Assert.Throws<ExcepcionDeDominio>(() => PoliticaDeContrasena.Validar(contrasena, "beto"));
    }

    [Fact]
    public void Rechaza_contrasenas_de_mas_de_128_caracteres()
    {
        Assert.Throws<ExcepcionDeDominio>(() => PoliticaDeContrasena.Validar(new string('x', 129), "beto"));
    }

    [Fact]
    public void Rechaza_una_contrasena_que_contiene_el_nombre_de_usuario()
    {
        Assert.Throws<ExcepcionDeDominio>(() => PoliticaDeContrasena.Validar("Robertito-BETO-2026", "beto"));
    }

    [Fact]
    public void Acepta_una_frase_larga_sin_reglas_de_composicion()
    {
        var aceptada = PoliticaDeContrasena.Validar("caballo correcto bateria grapa", "beto");

        Assert.Equal("caballo correcto bateria grapa", aceptada.Valor);
        Assert.DoesNotContain("caballo", aceptada.ToString());
    }
}
