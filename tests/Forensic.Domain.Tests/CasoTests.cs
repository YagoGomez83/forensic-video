using Forensic.Domain.Casos;
using Forensic.Domain.Comun;
using static Forensic.Domain.Tests.Fixtures;

namespace Forensic.Domain.Tests;

public class CasoTests
{
    [Fact]
    public void El_analista_que_abre_el_caso_queda_asignado()
    {
        var caso = CasoDeAna();

        Assert.Equal(["ana"], caso.AnalistasAsignados);
        Assert.True(caso.PuedeOperar(Ana));
    }

    [Fact]
    public void Un_analista_no_asignado_no_lee_ni_opera()
    {
        var caso = CasoDeAna();

        Assert.False(caso.PuedeLeer(Beto));
        Assert.False(caso.PuedeOperar(Beto));
    }

    [Fact]
    public void Supervisor_y_auditor_leen_todo_pero_no_operan()
    {
        var caso = CasoDeAna();

        Assert.True(caso.PuedeLeer(Supervisora));
        Assert.True(caso.PuedeLeer(Auditor));
        Assert.False(caso.PuedeOperar(Supervisora));
        Assert.False(caso.PuedeOperar(Auditor));
    }

    [Fact]
    public void Solo_un_supervisor_asigna_analistas()
    {
        var caso = CasoDeAna();

        Assert.Throws<AccesoDenegadoException>(() => caso.AsignarAnalista("beto", Ana));
        caso.AsignarAnalista("beto", Supervisora);
        Assert.True(caso.PuedeOperar(Beto));
    }

    [Fact]
    public void Un_auditor_no_abre_casos()
    {
        Assert.Throws<AccesoDenegadoException>(() => Caso.Abrir("EXP-1", "", Auditor, Ahora));
    }

    [Fact]
    public void Un_caso_cerrado_se_lee_pero_no_se_opera()
    {
        var caso = CasoDeAna();

        caso.Cerrar(Supervisora);

        Assert.Equal(EstadoCaso.Cerrado, caso.Estado);
        Assert.True(caso.PuedeLeer(Ana));
        Assert.False(caso.PuedeOperar(Ana));
        Assert.Throws<ExcepcionDeDominio>(() => caso.AsignarAnalista("beto", Supervisora));
    }

    [Fact]
    public void Un_supervisor_completa_cruces_pendientes_aunque_el_caso_este_cerrado()
    {
        var caso = CasoDeAna();
        Assert.True(caso.PuedeCompletarControlCruzado(Supervisora));

        caso.Cerrar(Supervisora);

        Assert.True(caso.PuedeCompletarControlCruzado(Supervisora));
        caso.ExigirCompletarControlCruzado(Supervisora);
    }

    [Fact]
    public void El_analista_asignado_completa_cruces_solo_con_el_caso_abierto()
    {
        var caso = CasoDeAna();
        Assert.True(caso.PuedeCompletarControlCruzado(Ana));
        Assert.False(caso.PuedeCompletarControlCruzado(Beto));

        caso.Cerrar(Supervisora);

        Assert.False(caso.PuedeCompletarControlCruzado(Ana));
        Assert.Throws<AccesoDenegadoException>(() => caso.ExigirCompletarControlCruzado(Ana));
    }

    [Fact]
    public void Un_auditor_nunca_completa_cruces()
    {
        var caso = CasoDeAna();
        Assert.False(caso.PuedeCompletarControlCruzado(Auditor));

        caso.Cerrar(Supervisora);

        Assert.Throws<AccesoDenegadoException>(() => caso.ExigirCompletarControlCruzado(Auditor));
    }

    [Fact]
    public void Un_admin_administra_usuarios_pero_no_accede_a_casos()
    {
        var caso = CasoDeAna();

        Assert.Throws<AccesoDenegadoException>(() => Caso.Abrir("EXP-2", "", Admin, Ahora));
        Assert.False(caso.PuedeLeer(Admin));
        Assert.False(caso.PuedeOperar(Admin));
        Assert.False(caso.PuedeCompletarControlCruzado(Admin));
        Assert.Throws<AccesoDenegadoException>(() => caso.AsignarAnalista("beto", Admin));
    }

    [Fact]
    public void El_analista_asignado_emite_el_informe_pericial_solo_con_el_caso_abierto()
    {
        var caso = CasoDeAna();
        caso.ExigirEmisionDeInformePericial(Ana);
        Assert.Throws<AccesoDenegadoException>(() => caso.ExigirEmisionDeInformePericial(Beto));

        caso.Cerrar(Supervisora);

        Assert.Throws<AccesoDenegadoException>(() => caso.ExigirEmisionDeInformePericial(Ana));
    }

    [Fact]
    public void Un_supervisor_emite_el_informe_pericial_en_cualquier_estado()
    {
        var caso = CasoDeAna();
        caso.ExigirEmisionDeInformePericial(Supervisora);

        caso.Cerrar(Supervisora);

        caso.ExigirEmisionDeInformePericial(Supervisora);
    }

    [Fact]
    public void Ni_el_auditor_ni_el_admin_emiten_informes()
    {
        var caso = CasoDeAna();

        Assert.Throws<AccesoDenegadoException>(() => caso.ExigirEmisionDeInformePericial(Auditor));
        Assert.Throws<AccesoDenegadoException>(() => caso.ExigirEmisionDeInformePericial(Admin));
    }

    [Fact]
    public void El_informe_investigativo_lo_emiten_el_analista_asignado_con_el_caso_abierto_y_el_supervisor_siempre()
    {
        var caso = CasoDeAna();
        caso.ExigirEmisionDeInformeInvestigativo(Ana);
        Assert.Throws<AccesoDenegadoException>(() => caso.ExigirEmisionDeInformeInvestigativo(Beto));
        Assert.Throws<AccesoDenegadoException>(() => caso.ExigirEmisionDeInformeInvestigativo(Auditor));

        caso.Cerrar(Supervisora);

        Assert.Throws<AccesoDenegadoException>(() => caso.ExigirEmisionDeInformeInvestigativo(Ana));
        caso.ExigirEmisionDeInformeInvestigativo(Supervisora);
    }
}
