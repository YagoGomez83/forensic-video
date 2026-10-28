using Forensic.Domain.Comun;
using Forensic.Domain.Identidad;
using static Forensic.Domain.Tests.Fixtures;

namespace Forensic.Domain.Tests;

/// <summary>
/// La auditoría de accesos encadenada por hash (forensic-video/accesos/v1). La base calcula los hashes
/// al insertar; esto los recalcula de forma independiente para verificarla.
/// </summary>
public class CadenaDeAccesosTests
{
    private static List<AccesoEncadenado> Cadena(int largo)
    {
        var cadena = new List<AccesoEncadenado>();
        var anterior = HashSha256.Genesis;
        for (var i = 1; i <= largo; i++)
        {
            var a = new AccesoEncadenado(i, "LoginFallido", $"usuario{i}", $"usuario{i}", i % 2 == 0 ? null : "contraseña inválida",
                "10.0.0.1", Ahora.AddSeconds(i), anterior, default);
            a = a with { Hash = CadenaDeAccesos.CalcularHash(a) };
            cadena.Add(a);
            anterior = a.Hash;
        }
        return cadena;
    }

    [Fact]
    public void Una_cadena_bien_encadenada_verifica()
    {
        var cadena = Cadena(4);

        var v = CadenaDeAccesos.Verificar(cadena);

        Assert.True(v.EsIntegra);
        Assert.Equal(4, v.CantidadAsientos);
        Assert.Equal(cadena[^1].Hash, v.HashFinal);
    }

    [Theory]
    [InlineData("usuario")]
    [InlineData("actor")]
    [InlineData("detalle")]
    [InlineData("origen")]
    [InlineData("tipo")]
    [InlineData("timestamp")]
    public void Alterar_un_campo_se_detecta_en_esa_fila(string campo)
    {
        var cadena = Cadena(3);
        var b = cadena[1];
        cadena[1] = campo switch
        {
            "usuario" => b with { Usuario = "otro" },
            "actor" => b with { Actor = "otro" },
            "detalle" => b with { Detalle = "otro" },
            "origen" => b with { Origen = "10.0.0.2" },
            "tipo" => b with { Tipo = "LoginExitoso" },
            _ => b with { Timestamp = b.Timestamp.AddMicroseconds(1) },
        };

        var v = CadenaDeAccesos.Verificar(cadena);

        Assert.False(v.EsIntegra);
        Assert.Equal(2, v.SecuenciaRota);
    }

    [Fact]
    public void Borrar_una_fila_intermedia_se_detecta()
    {
        var cadena = Cadena(4);
        cadena.RemoveAt(1);

        var v = CadenaDeAccesos.Verificar(cadena);

        Assert.False(v.EsIntegra);
        Assert.Equal(3, v.SecuenciaRota);
    }

    [Fact]
    public void Un_detalle_nulo_y_uno_vacio_no_hashean_igual()
    {
        var a = Cadena(1)[0] with { Detalle = null };

        Assert.NotEqual(CadenaDeAccesos.CalcularHash(a), CadenaDeAccesos.CalcularHash(a with { Detalle = "" }));
    }

    [Fact]
    public void Se_sella_como_una_cadena_de_custodia_y_una_reconstruida_no_coincide_con_su_sello()
    {
        var clave = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        byte[] Firmar(byte[] m) => System.Security.Cryptography.HMACSHA256.HashData(clave, m);
        bool Valida(Custodia.SelloDeCadena s) => s.Firma.AsSpan().SequenceEqual(Firmar(s.MensajeCanonico()));
        var cadena = Cadena(3);
        var sello = Custodia.SelloDeCadena.Sellar(CadenaDeAccesos.IdParaSellos, 3, cadena[^1].Hash, Ahora, "ed25519:prueba", Firmar);

        var buena = Custodia.RegistroDeCustodia.VerificarSellos(
            CadenaDeAccesos.IdParaSellos, cadena.Select(a => (a.Secuencia, a.Hash)).ToList(), [sello], Valida);
        var reconstruida = Cadena(3).Select((a, i) => i == 0 ? a with { Usuario = "x" } : a).ToList();
        var mala = Custodia.RegistroDeCustodia.VerificarSellos(
            CadenaDeAccesos.IdParaSellos, Recalcular(reconstruida).Select(a => (a.Secuencia, a.Hash)).ToList(), [sello], Valida);

        Assert.True(buena.SonValidos);
        Assert.True(CadenaDeAccesos.Verificar(Recalcular(reconstruida)).EsIntegra);
        Assert.False(mala.SonValidos);
    }

    private static List<AccesoEncadenado> Recalcular(List<AccesoEncadenado> cadena)
    {
        var anterior = HashSha256.Genesis;
        return cadena.Select(a =>
        {
            var b = a with { HashAnterior = anterior };
            b = b with { Hash = CadenaDeAccesos.CalcularHash(b) };
            anterior = b.Hash;
            return b;
        }).ToList();
    }

    [Fact]
    public void Una_cadena_vacia_es_integra()
    {
        Assert.True(CadenaDeAccesos.Verificar([]).EsIntegra);
    }
}
