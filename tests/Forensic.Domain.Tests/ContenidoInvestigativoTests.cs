using Forensic.Domain.Comun;
using Forensic.Domain.Custodia;
using Forensic.Domain.Evidencias;
using Forensic.Domain.Informes;

namespace Forensic.Domain.Tests;

/// <summary>
/// El informe investigativo: lo que el pericial excluye, junto con el material admisible que su linaje
/// necesita, todo marcado «sólo investigación». Es otro tipo: no se convierte en un pericial.
/// </summary>
public class ContenidoInvestigativoTests
{
    private readonly CasoDeDosVias _c = new();

    private ContenidoInvestigativo Armar() => ContenidoInvestigativo.Armar(_c.Caso, _c.Evidencias, _c.Cadena);

    [Fact]
    public void Incluye_las_evidencias_de_las_dos_vias()
    {
        var c = Armar();

        Assert.Equal([_c.Foto.Id, _c.Recortada.Id, _c.SuperRes.Id, _c.RotacionDeLaSuperRes.Id], c.Evidencias.Select(e => e.Evidencia.Id));
    }

    [Fact]
    public void Contiene_lo_que_el_pericial_excluye()
    {
        var pericial = ContenidoPericial.Armar(_c.Caso, _c.Evidencias, _c.Cadena);
        var c = Armar();

        Assert.Superset(pericial.EvidenciasExcluidas.ToHashSet(), c.Evidencias.Select(e => e.Evidencia.Id).ToHashSet());
        Assert.Superset(
            pericial.Asientos.Where(a => a.Opaco).Select(a => a.Asiento.Secuencia).ToHashSet(),
            c.Asientos.Where(a => !a.Opaco).Select(a => a.Asiento.Secuencia).ToHashSet());
    }

    [Fact]
    public void Todos_los_asientos_van_completos()
    {
        var c = Armar();

        Assert.Equal(_c.Cadena.Select(a => a.Secuencia), c.Asientos.Select(a => a.Asiento.Secuencia));
        Assert.All(c.Asientos, a => Assert.False(a.Opaco));
    }

    [Fact]
    public void Un_derivado_de_ia_sale_de_su_operacion_de_ia()
    {
        var c = Armar();
        var origen = c.Evidencias.Single(e => e.Evidencia.Id == _c.SuperRes.Id).AsientoDeOrigen;

        Assert.Equal(TipoAsiento.OperacionIaAplicada, _c.Cadena.Single(a => a.Secuencia == origen).Tipo);
    }

    [Fact]
    public void Esta_marcado_solo_investigacion()
    {
        Assert.Equal(ViaProcesamiento.SoloInvestigacion, Armar().Via);
    }

    [Fact]
    public void Una_evidencia_sin_su_asiento_de_origen_no_puede_informarse()
    {
        var sinIa = _c.Cadena.Where(a => a.Tipo != TipoAsiento.OperacionIaAplicada).ToList();

        Assert.Throws<ExcepcionDeDominio>(() => ContenidoInvestigativo.Armar(_c.Caso, _c.Evidencias, sinIa));
    }

    [Fact]
    public void Evidencias_de_otro_caso_no_se_mezclan()
    {
        Assert.Throws<ExcepcionDeDominio>(() => ContenidoInvestigativo.Armar(Fixtures.CasoDeAna(), _c.Evidencias, _c.Cadena));
    }
}
