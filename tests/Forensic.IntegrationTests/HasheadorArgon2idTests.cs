using Forensic.Domain.Identidad;
using Forensic.Infrastructure.Identidad;

namespace Forensic.IntegrationTests;

public class HasheadorArgon2idTests
{
    private const string Contrasena = "caballo correcto bateria grapa";

    private readonly HasheadorArgon2id _hasheador = new();

    [Fact]
    public void El_hash_es_argon2id_con_sal_y_nunca_contiene_la_contrasena()
    {
        var hash = _hasheador.Hashear(PoliticaDeContrasena.Validar(Contrasena, "beto"));

        Assert.StartsWith("$argon2id$v=19$", hash.Phc);
        Assert.DoesNotContain(Contrasena, hash.Phc);
        // Con sal aleatoria, la misma contraseña da otro hash.
        Assert.NotEqual(hash, _hasheador.Hashear(PoliticaDeContrasena.Validar(Contrasena, "beto")));
    }

    [Fact]
    public void Verifica_la_contrasena_correcta_y_rechaza_otra()
    {
        var hash = _hasheador.Hashear(PoliticaDeContrasena.Validar(Contrasena, "beto"));

        Assert.True(_hasheador.Verificar(Contrasena, hash));
        Assert.False(_hasheador.Verificar("caballo correcto bateria grapA", hash));
        Assert.False(_hasheador.Verificar("", hash));
    }

    [Fact]
    public void El_hash_ficticio_no_verifica_ninguna_contrasena_conocida()
    {
        Assert.StartsWith("$argon2id$", _hasheador.HashFicticio.Phc);
        Assert.False(_hasheador.Verificar(Contrasena, _hasheador.HashFicticio));
        Assert.False(_hasheador.Verificar("", _hasheador.HashFicticio));
    }
}
