using System.Security.Cryptography;
using Forensic.Domain.Comun;
using Forensic.Domain.Custodia;
using Forensic.Infrastructure.Sellado;
using Sodium;

namespace Forensic.IntegrationTests;

public class FirmanteEd25519Tests
{
    // RFC 8032, sección 7.1, TEST 1: fija que la firma es Ed25519 estándar, la que cualquier
    // verificador de terceros (el del bundle incluido) tiene que aceptar.
    private static readonly byte[] SemillaRfc = Convert.FromHexString("9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60");
    private const string PublicaRfc = "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a";
    private const string FirmaRfcDelMensajeVacio =
        "e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e065224901555fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b";

    private static readonly DateTimeOffset Ahora = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    private static SelloDeCadena SelloDe(FirmanteEd25519 firmante)
    {
        var cabeza = RegistroDeCustodia.Asentar(
            null, Guid.NewGuid(), TipoAsiento.CasoAbierto, null, "{}", new Actor("ana", Rol.Analista), Ahora);
        return SelloDeCadena.Sellar(cabeza, Ahora, firmante.ClaveId, firmante.Firmar);
    }

    [Fact]
    public void Firma_ed25519_estandar_segun_el_vector_del_rfc_8032()
    {
        var firmante = FirmanteEd25519.DesdePem(ClaveDePrueba.Pem(SemillaRfc));

        Assert.Equal(PublicaRfc, Convert.ToHexString(firmante.ClavePublica).ToLowerInvariant());
        Assert.Equal(FirmaRfcDelMensajeVacio, Convert.ToHexString(firmante.Firmar([])).ToLowerInvariant());
    }

    /// <summary>Vector de docs/formato-bundle-v1.md: el sello del asiento del vector, firmado con la clave del RFC.</summary>
    [Fact]
    public void La_firma_del_sello_coincide_con_el_vector_publicado()
    {
        var firmante = FirmanteEd25519.DesdePem(ClaveDePrueba.Pem(SemillaRfc));
        var sello = SelloDeCadena.Rehidratar(
            Guid.Parse("22222222-2222-2222-2222-222222222222"), 1,
            HashSha256.Parse("5949e6267194282c3eaaac6e9730ed1779ab2fd20d3b20d1bc02cbe04dec2b0a"),
            new DateTimeOffset(2026, 10, 28, 13, 0, 0, TimeSpan.Zero), firmante.ClaveId, []);

        Assert.Equal("ed25519:21fe31dfa154a261626bf854046fd2271b7bed4b6abe45aa58877ef47f9721b9", firmante.ClaveId);
        Assert.Equal("61908f5f1905f2742aa988653dc556bfe09e5ec62bb07311eef3b7646ac7b4256329bfae5c3069c0626f5e69260f132e1d53defb46774e3336831f06bc75580c", Convert.ToHexString(firmante.Firmar(sello.MensajeCanonico())).ToLowerInvariant());
    }

    [Fact]
    public void El_id_de_la_clave_es_el_sha256_de_la_clave_publica()
    {
        var firmante = FirmanteEd25519.DesdePem(ClaveDePrueba.Pem(SemillaRfc));

        Assert.Equal("ed25519:" + Convert.ToHexString(SHA256.HashData(Convert.FromHexString(PublicaRfc))).ToLowerInvariant(),
            firmante.ClaveId);
    }

    [Fact]
    public void Un_sello_verifica_contra_la_clave_publica()
    {
        var firmante = FirmanteEd25519.DesdePem(ClaveDePrueba.Nueva());
        var sello = SelloDe(firmante);

        // Con la pública sola, sin el firmante: es lo que hace un tercero.
        Assert.True(PublicKeyAuth.VerifyDetached(sello.Firma, sello.MensajeCanonico(), firmante.ClavePublica));
        Assert.True(firmante.Verificar(sello));
    }

    [Fact]
    public void Un_sello_adulterado_o_de_otra_clave_no_verifica()
    {
        var firmante = FirmanteEd25519.DesdePem(ClaveDePrueba.Nueva());
        var sello = SelloDe(firmante);
        var adulterado = SelloDeCadena.Rehidratar(
            sello.CasoId, sello.Secuencia + 1, sello.HashCabeza, sello.SelladoEn, sello.ClaveId, sello.Firma);
        var ajeno = SelloDe(FirmanteEd25519.DesdePem(ClaveDePrueba.Nueva()));

        Assert.False(firmante.Verificar(adulterado));
        Assert.False(firmante.Verificar(ajeno));
    }

    [Fact]
    public void Un_sello_con_el_id_de_la_clave_pero_firma_basura_no_verifica()
    {
        var firmante = FirmanteEd25519.DesdePem(ClaveDePrueba.Nueva());
        var sello = SelloDe(firmante);
        var basura = SelloDeCadena.Rehidratar(
            sello.CasoId, sello.Secuencia, sello.HashCabeza, sello.SelladoEn, sello.ClaveId, [1, 2, 3]);

        Assert.False(firmante.Verificar(basura));
    }

    [Theory]
    [InlineData("")]
    [InlineData("no es un pem")]
    [InlineData("-----BEGIN PRIVATE KEY-----\nAAAA\n-----END PRIVATE KEY-----\n")]
    public void Una_clave_que_no_es_ed25519_en_pkcs8_se_rechaza(string pem)
    {
        Assert.Throws<InvalidOperationException>(() => FirmanteEd25519.DesdePem(pem));
    }

    [Fact]
    public void Una_clave_ec_valida_en_pkcs8_tampoco_se_acepta()
    {
        // Generada en el test, no escrita en el código: es una privada válida, pero no es Ed25519.
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        Assert.Throws<InvalidOperationException>(() => FirmanteEd25519.DesdePem(ec.ExportPkcs8PrivateKeyPem()));
    }

    [Fact]
    public void Sin_archivo_de_clave_no_hay_firmante()
    {
        var ruta = Path.Combine(Path.GetTempPath(), $"no-existe-{Guid.NewGuid():N}.pem");

        var ex = Assert.Throws<InvalidOperationException>(() => FirmanteEd25519.DesdeArchivo(ruta));
        Assert.Contains("clave de sellado", ex.Message);
    }
}
