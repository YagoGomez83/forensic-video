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
}
