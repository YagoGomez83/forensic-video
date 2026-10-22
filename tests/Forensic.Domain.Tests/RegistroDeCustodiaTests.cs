using Forensic.Domain.Comun;
using Forensic.Domain.Custodia;
using static Forensic.Domain.Tests.Fixtures;

namespace Forensic.Domain.Tests;

public class RegistroDeCustodiaTests
{
    private static readonly Guid CasoId = Guid.NewGuid();

    private static List<AsientoDeCustodia> Cadena(int largo)
    {
        var cadena = new List<AsientoDeCustodia>();
        for (var i = 0; i < largo; i++)
        {
            cadena.Add(RegistroDeCustodia.Asentar(
                cadena.LastOrDefault(), CasoId, TipoAsiento.EvidenciaIngestada, Guid.NewGuid(),
                $$"""{"n":{{i}}}""", Ana, Ahora.AddSeconds(i)));
        }
        return cadena;
    }

    private static AsientoDeCustodia Copia(AsientoDeCustodia a, string? detalle = null, long? secuencia = null, string? actor = null) =>
        new(a.Id, a.CasoId, secuencia ?? a.Secuencia, a.Tipo, a.EvidenciaId, detalle ?? a.Detalle,
            actor ?? a.Actor, a.Timestamp, a.HashAnterior, a.Hash);

    [Fact]
    public void Una_cadena_recien_asentada_verifica()
    {
        var cadena = Cadena(3);

        var v = RegistroDeCustodia.Verificar(CasoId, cadena);

        Assert.True(v.EsIntegra);
        Assert.Equal(3, v.CantidadAsientos);
        Assert.Equal(cadena[^1].Hash, v.HashFinal);
    }

    [Fact]
    public void El_primer_asiento_encadena_al_genesis_y_los_siguientes_al_anterior()
    {
        var cadena = Cadena(3);

        Assert.Equal(HashSha256.Genesis, cadena[0].HashAnterior);
        Assert.Equal(new long[] { 1, 2, 3 }, cadena.Select(a => a.Secuencia));
        Assert.Equal(cadena[0].Hash, cadena[1].HashAnterior);
        Assert.Equal(cadena[1].Hash, cadena[2].HashAnterior);
    }

    [Fact]
    public void Alterar_el_detalle_de_un_asiento_se_detecta_en_ese_asiento()
    {
        var cadena = Cadena(3);
        cadena[1] = Copia(cadena[1], detalle: """{"n":99}""");

        var v = RegistroDeCustodia.Verificar(CasoId, cadena);

        Assert.False(v.EsIntegra);
        Assert.Equal(2, v.SecuenciaRota);
    }

    [Fact]
    public void Alterar_el_actor_se_detecta()
    {
        var cadena = Cadena(2);
        cadena[0] = Copia(cadena[0], actor: "otro");

        Assert.Equal(1, RegistroDeCustodia.Verificar(CasoId, cadena).SecuenciaRota);
    }

    [Fact]
    public void Eliminar_un_asiento_intermedio_se_detecta()
    {
        var cadena = Cadena(3);
        cadena.RemoveAt(1);

        var v = RegistroDeCustodia.Verificar(CasoId, cadena);

        Assert.False(v.EsIntegra);
        Assert.Equal(3, v.SecuenciaRota);
    }

    [Fact]
    public void Recalcular_el_hash_de_un_asiento_alterado_rompe_al_siguiente()
    {
        var cadena = Cadena(3);
        var a = cadena[1];
        var falsificado = AsientoDeCustodia.Crear(a.CasoId, a.Secuencia, a.Tipo, a.EvidenciaId, """{"n":99}""", a.Actor, a.Timestamp, a.HashAnterior);
        cadena[1] = falsificado;

        var v = RegistroDeCustodia.Verificar(CasoId, cadena);

        Assert.False(v.EsIntegra);
        Assert.Equal(3, v.SecuenciaRota);
    }

    [Fact]
    public void Un_detalle_con_saltos_de_linea_no_puede_simular_otro_campo()
    {
        var a = RegistroDeCustodia.Asentar(null, CasoId, TipoAsiento.CasoAbierto, null, "x\nactor:3:eve", Ana, Ahora);
        var b = RegistroDeCustodia.Asentar(null, CasoId, TipoAsiento.CasoAbierto, null, "x", new Actor("eve", Rol.Analista), Ahora);

        Assert.NotEqual(a.CalcularHash(), Copia(b, detalle: "x\nactor:3:eve").CalcularHash());
    }

    [Fact]
    public void El_timestamp_se_guarda_en_UTC_con_precision_de_microsegundos()
    {
        var conTicks = Ahora.AddTicks(1234567);

        var a = RegistroDeCustodia.Asentar(null, CasoId, TipoAsiento.CasoAbierto, null, "{}", Ana, conTicks);

        Assert.Equal(TimeSpan.Zero, a.Timestamp.Offset);
        Assert.Equal(0, a.Timestamp.Ticks % 10);
        Assert.Equal(conTicks.UtcTicks - conTicks.UtcTicks % 10, a.Timestamp.UtcTicks);
    }

    [Fact]
    public void No_se_puede_encadenar_a_un_asiento_de_otro_caso()
    {
        var ajeno = Cadena(1)[0];

        Assert.Throws<ExcepcionDeDominio>(() =>
            RegistroDeCustodia.Asentar(ajeno, Guid.NewGuid(), TipoAsiento.CasoAbierto, null, "{}", Ana, Ahora));
    }

    [Fact]
    public void Un_reloj_que_retrocede_no_puede_asentar()
    {
        var ultimo = Cadena(1)[0];

        Assert.Throws<ExcepcionDeDominio>(() =>
            RegistroDeCustodia.Asentar(ultimo, CasoId, TipoAsiento.EvidenciaAccedida, null, "{}", Ana, Ahora.AddMinutes(-1)));
    }
}
