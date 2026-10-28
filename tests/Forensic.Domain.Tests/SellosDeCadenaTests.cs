using System.Security.Cryptography;
using Forensic.Domain.Comun;
using Forensic.Domain.Custodia;
using static Forensic.Domain.Tests.Fixtures;

namespace Forensic.Domain.Tests;

public class SellosDeCadenaTests
{
    private static readonly Guid CasoId = Guid.NewGuid();
    private const string ClaveId = "ed25519:prueba";

    // Doble de la firma del laboratorio: el dominio no sabe de Ed25519, sólo firma y verifica bytes.
    private static readonly byte[] Clave = RandomNumberGenerator.GetBytes(32);
    private static byte[] Firmar(byte[] mensaje) => HMACSHA256.HashData(Clave, mensaje);
    private static bool FirmaValida(SelloDeCadena s) =>
        CryptographicOperations.FixedTimeEquals(s.Firma, HMACSHA256.HashData(Clave, s.MensajeCanonico()));

    private static List<AsientoDeCustodia> Cadena(int largo, string marca = "n")
    {
        var cadena = new List<AsientoDeCustodia>();
        for (var i = 0; i < largo; i++)
        {
            cadena.Add(RegistroDeCustodia.Asentar(
                cadena.LastOrDefault(), CasoId, TipoAsiento.EvidenciaIngestada, null,
                $$"""{"{{marca}}":{{i}}}""", Ana, Ahora.AddSeconds(i)));
        }
        return cadena;
    }

    private static SelloDeCadena Sellar(AsientoDeCustodia cabeza) =>
        SelloDeCadena.Sellar(cabeza, Ahora.AddHours(1), ClaveId, Firmar);

    private static SelloDeCadena Copia(SelloDeCadena s, HashSha256? hash = null, long? secuencia = null, Guid? caso = null) =>
        SelloDeCadena.Rehidratar(caso ?? s.CasoId, secuencia ?? s.Secuencia, hash ?? s.HashCabeza, s.SelladoEn, s.ClaveId, s.Firma);

    [Fact]
    public void El_sello_de_la_cabeza_verifica_contra_su_cadena()
    {
        var cadena = Cadena(3);
        var sello = Sellar(cadena[^1]);

        var v = RegistroDeCustodia.VerificarSellos(CasoId, cadena, [sello], FirmaValida);

        Assert.True(v.SonValidos);
        Assert.Equal(1, v.CantidadSellos);
        Assert.Equal(3, v.UltimaSecuenciaSellada);
        Assert.Equal(0, v.AsientosSinSellar);
    }

    [Fact]
    public void El_sello_toma_caso_secuencia_y_hash_de_la_cabeza_y_un_timestamp_normalizado()
    {
        var cadena = Cadena(2);

        var sello = SelloDeCadena.Sellar(cadena[^1], Ahora.AddTicks(7), ClaveId, Firmar);

        Assert.Equal(CasoId, sello.CasoId);
        Assert.Equal(2, sello.Secuencia);
        Assert.Equal(cadena[^1].Hash, sello.HashCabeza);
        Assert.Equal(Tiempo.NormalizarParaCustodia(Ahora), sello.SelladoEn);
        Assert.Equal(ClaveId, sello.ClaveId);
    }

    [Fact]
    public void Una_cadena_reconstruida_entera_verifica_por_si_misma_pero_no_contra_el_sello()
    {
        var original = Cadena(4);
        var sello = Sellar(original[2]);

        // Quien reescribe la base recalcula todos los hashes: la cadena nueva es íntegra.
        var reconstruida = Cadena(4, marca: "falsificado");
        Assert.True(RegistroDeCustodia.Verificar(CasoId, reconstruida).EsIntegra);

        var v = RegistroDeCustodia.VerificarSellos(CasoId, reconstruida, [sello], FirmaValida);

        Assert.False(v.SonValidos);
        Assert.Equal(3, v.SecuenciaRota);
        Assert.Contains("reconstruida", v.Motivo);
    }

    [Fact]
    public void Ajustar_el_hash_del_sello_a_la_cadena_reconstruida_rompe_su_firma()
    {
        var sello = Sellar(Cadena(3)[^1]);
        var reconstruida = Cadena(3, marca: "falsificado");

        var adulterado = Copia(sello, hash: reconstruida[^1].Hash);
        var v = RegistroDeCustodia.VerificarSellos(CasoId, reconstruida, [adulterado], FirmaValida);

        Assert.False(v.SonValidos);
        Assert.Equal(3, v.SecuenciaRota);
        Assert.Contains("firma", v.Motivo);
    }

    [Fact]
    public void Truncar_la_cadena_por_debajo_de_un_sello_se_detecta()
    {
        var cadena = Cadena(5);
        var sello = Sellar(cadena[^1]);

        var v = RegistroDeCustodia.VerificarSellos(CasoId, cadena.Take(3).ToList(), [sello], FirmaValida);

        Assert.False(v.SonValidos);
        Assert.Equal(5, v.SecuenciaRota);
        Assert.Contains("truncada", v.Motivo);
    }

    [Fact]
    public void Un_sello_de_otro_caso_no_vale_para_esta_cadena()
    {
        var cadena = Cadena(2);
        var sello = Sellar(cadena[^1]);

        var v = RegistroDeCustodia.VerificarSellos(CasoId, cadena, [Copia(sello, caso: Guid.NewGuid())], FirmaValida);

        Assert.False(v.SonValidos);
        Assert.Contains("otro caso", v.Motivo);
    }

    [Fact]
    public void Los_asientos_posteriores_al_ultimo_sello_se_informan_como_sin_sellar()
    {
        var cadena = Cadena(6);
        var sellos = new[] { Sellar(cadena[1]), Sellar(cadena[3]) };

        var v = RegistroDeCustodia.VerificarSellos(CasoId, cadena, sellos, FirmaValida);

        Assert.True(v.SonValidos);
        Assert.Equal(2, v.CantidadSellos);
        Assert.Equal(4, v.UltimaSecuenciaSellada);
        Assert.Equal(2, v.AsientosSinSellar);
    }

    [Fact]
    public void Una_cadena_sin_sellos_es_valida_pero_queda_toda_sin_sellar()
    {
        var v = RegistroDeCustodia.VerificarSellos(CasoId, Cadena(3), [], FirmaValida);

        Assert.True(v.SonValidos);
        Assert.Null(v.UltimaSecuenciaSellada);
        Assert.Equal(3, v.AsientosSinSellar);
    }

    [Fact]
    public void El_mensaje_firmado_cubre_cada_campo_del_sello()
    {
        var sello = Sellar(Cadena(2)[^1]);
        var otroHash = HashDe("otro");

        var variantes = new[]
        {
            Copia(sello, hash: otroHash),
            Copia(sello, secuencia: 1),
            Copia(sello, caso: Guid.NewGuid()),
            SelloDeCadena.Rehidratar(sello.CasoId, sello.Secuencia, sello.HashCabeza, sello.SelladoEn.AddSeconds(1), sello.ClaveId, sello.Firma),
            SelloDeCadena.Rehidratar(sello.CasoId, sello.Secuencia, sello.HashCabeza, sello.SelladoEn, "ed25519:otra", sello.Firma),
        };

        Assert.All(variantes, v => Assert.False(sello.MensajeCanonico().AsSpan().SequenceEqual(v.MensajeCanonico())));
    }

    [Fact]
    public void Un_sello_exige_una_firma()
    {
        var cabeza = Cadena(1)[^1];

        Assert.Throws<ExcepcionDeDominio>(() => SelloDeCadena.Sellar(cabeza, Ahora, ClaveId, _ => []));
        Assert.Throws<ExcepcionDeDominio>(() => SelloDeCadena.Sellar(cabeza, Ahora, " ", Firmar));
    }
}
