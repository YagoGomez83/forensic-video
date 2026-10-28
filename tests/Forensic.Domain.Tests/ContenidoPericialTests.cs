using System.Text.Json;
using Forensic.Domain.Casos;
using Forensic.Domain.Comun;
using Forensic.Domain.Custodia;
using Forensic.Domain.Evidencias;
using Forensic.Domain.Ia;
using Forensic.Domain.Informes;
using Forensic.Domain.Operaciones;
using static Forensic.Domain.Tests.Fixtures;

namespace Forensic.Domain.Tests;

/// <summary>
/// Qué entra a un informe pericial. ExigirAdmisibleEnJuicio es la única puerta de las evidencias, y
/// sólo una lista blanca cerrada de tipos de asiento puede mostrarse completa.
/// </summary>
public class ContenidoPericialTests
{
    private readonly Caso _caso = CasoDeAna();
    private readonly List<AsientoDeCustodia> _cadena = [];
    private readonly List<Evidencia> _evidencias = [];

    private readonly Evidencia _original;
    private readonly Evidencia _recorte;
    private readonly Evidencia _superRes;
    private readonly Evidencia _rotacionDeLaSuperRes;

    public ContenidoPericialTests()
    {
        Asentar(TipoAsiento.CasoAbierto, null);
        _original = Original(_caso);
        Agregar(_original, TipoAsiento.EvidenciaIngestada);
        _recorte = Derivar(_original, OpenCv(_original, "recorte"), "recorte.png");
        Agregar(_recorte, TipoAsiento.OperacionAplicada);
        Asentar(TipoAsiento.HallazgosRegistrados, _original.Id);
        _superRes = Derivar(_recorte, Ia(_recorte), "recorte-sr.png");
        Agregar(_superRes, TipoAsiento.OperacionIaAplicada);
        _rotacionDeLaSuperRes = Derivar(_superRes, OpenCv(_superRes, "rotada"), "sr-rotada.png");
        Agregar(_rotacionDeLaSuperRes, TipoAsiento.OperacionAplicada);
        Asentar(TipoAsiento.EvidenciaAccedida, _superRes.Id);
        Asentar(TipoAsiento.EvidenciaAccedida, _recorte.Id);
        Asentar(TipoAsiento.AnalistaAsignado, null);
    }

    private void Asentar(TipoAsiento tipo, Guid? evidencia) =>
        _cadena.Add(RegistroDeCustodia.Asentar(
            _cadena.LastOrDefault(), _caso.Id, tipo, evidencia, "{}", Ana, Ahora.AddSeconds(_cadena.Count)));

    private void Agregar(Evidencia e, TipoAsiento tipo)
    {
        _evidencias.Add(e);
        Asentar(tipo, e.Id);
    }

    private OperacionForense OpenCv(Evidencia entrada, string salida) =>
        OperacionForense.Registrar(_caso, entrada, Recorte.Crear(0, 0, 1, 1), EntornoDePrueba, HashDe(salida), 512, null, Ana, Ahora);

    private OperacionForense Ia(Evidencia entrada) =>
        OperacionForense.RegistrarIa(
            _caso, entrada, CatalogoDeIa.InterpretarOperacion(CatalogoDeIa.SuperResolucion, JsonDocument.Parse("{}").RootElement),
            RealEsrgan, EntornoDePrueba, HashDe("sr"), 4096, Ana, Ahora);

    private static Evidencia Derivar(Evidencia padre, OperacionForense op, string nombre) => padre.Derivar(op, nombre);

    private ContenidoPericial Armar() => ContenidoPericial.Armar(_caso, _evidencias, _cadena);

    private AsientoDelInforme AsientoDe(ContenidoPericial c, TipoAsiento tipo, Guid? evidencia) =>
        c.Asientos.Single(a => a.Asiento.Tipo == tipo && a.Asiento.EvidenciaId == evidencia);

    [Fact]
    public void Sólo_entran_las_evidencias_admisibles_en_juicio()
    {
        var c = Armar();

        Assert.Equal([_original.Id, _recorte.Id], c.Evidencias.Select(e => e.Evidencia.Id));
        Assert.Equal([_superRes.Id, _rotacionDeLaSuperRes.Id], c.EvidenciasExcluidas);
        Assert.All(c.Evidencias, e => Assert.Equal(ViaProcesamiento.AdmisibleEnJuicio, e.Evidencia.Via));
    }

    [Fact]
    public void Cada_evidencia_lleva_el_asiento_que_la_registro()
    {
        var c = Armar();

        Assert.Equal(2, c.Evidencias[0].AsientoDeOrigen);
        Assert.Equal(3, c.Evidencias[1].AsientoDeOrigen);
    }

    [Fact]
    public void La_cadena_entra_entera_y_en_orden_con_los_asientos_de_investigacion_opacos()
    {
        var c = Armar();

        Assert.Equal(_cadena.Select(a => a.Secuencia), c.Asientos.Select(a => a.Asiento.Secuencia));
        Assert.True(AsientoDe(c, TipoAsiento.HallazgosRegistrados, _original.Id).Opaco);
        Assert.True(AsientoDe(c, TipoAsiento.OperacionIaAplicada, _superRes.Id).Opaco);
        // Una operación OpenCV es de un tipo de la lista blanca, pero sobre un derivado de IA: opaca.
        Assert.True(AsientoDe(c, TipoAsiento.OperacionAplicada, _rotacionDeLaSuperRes.Id).Opaco);
        Assert.True(AsientoDe(c, TipoAsiento.EvidenciaAccedida, _superRes.Id).Opaco);
        Assert.Equal(4, c.AsientosOpacos);
    }

    [Fact]
    public void Los_asientos_de_la_lista_blanca_sobre_evidencia_admisible_van_completos()
    {
        var c = Armar();

        Assert.False(AsientoDe(c, TipoAsiento.CasoAbierto, null).Opaco);
        Assert.False(AsientoDe(c, TipoAsiento.EvidenciaIngestada, _original.Id).Opaco);
        Assert.False(AsientoDe(c, TipoAsiento.OperacionAplicada, _recorte.Id).Opaco);
        Assert.False(AsientoDe(c, TipoAsiento.EvidenciaAccedida, _recorte.Id).Opaco);
        Assert.False(AsientoDe(c, TipoAsiento.AnalistaAsignado, null).Opaco);
    }

    [Fact]
    public void La_lista_blanca_es_cerrada_y_un_tipo_que_no_esta_en_ella_va_opaco()
    {
        Assert.Equal(
            [
                TipoAsiento.CasoAbierto, TipoAsiento.AnalistaAsignado, TipoAsiento.CasoCerrado, TipoAsiento.EvidenciaIngestada,
                TipoAsiento.EvidenciaAccedida, TipoAsiento.ControlCruzadoCompletado, TipoAsiento.OperacionAplicada,
                TipoAsiento.OperacionReproducida, TipoAsiento.InformePericialEmitido,
            ],
            ContenidoPericial.TiposCompletos.Order());
        foreach (var tipo in Enum.GetValues<TipoAsiento>().Where(t => !ContenidoPericial.TiposCompletos.Contains(t)))
            Assert.True(ContenidoPericial.EsOpaco(tipo, null, new HashSet<Guid>()), $"{tipo} debería ir opaco");
    }

    [Fact]
    public void La_lista_blanca_es_la_de_la_especificacion_que_aplica_el_verificador()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "docs", "formato-bundle-v1.md")))
            dir = dir.Parent;
        var linea = File.ReadLines(Path.Combine(dir!.FullName, "docs", "formato-bundle-v1.md")).Single(l => l.Contains("lista blanca cerrada:"));
        var enLaEspecificacion = System.Text.RegularExpressions.Regex.Matches(linea[linea.IndexOf(':')..], "`([A-Za-z]+)`")
            .Select(m => Enum.Parse<TipoAsiento>(m.Groups[1].Value));

        Assert.Equal(enLaEspecificacion.Order(), ContenidoPericial.TiposCompletos.Order());
    }

    [Fact]
    public void La_unica_puerta_es_el_guard_y_lanza_su_propia_excepcion()
    {
        var ex = Assert.Throws<EvidenciaSoloInvestigacionException>(_superRes.ExigirAdmisibleEnJuicio);

        Assert.IsAssignableFrom<ExcepcionDeDominio>(ex);
        Assert.Equal(_superRes.Id, ex.EvidenciaId);
    }

    [Fact]
    public void Una_evidencia_admisible_sin_su_asiento_de_origen_no_puede_presentarse()
    {
        var sinIngesta = _cadena.Where(a => a.Tipo != TipoAsiento.EvidenciaIngestada).ToList();

        Assert.Throws<ExcepcionDeDominio>(() => ContenidoPericial.Armar(_caso, _evidencias, sinIngesta));
    }

    [Fact]
    public void Evidencias_o_asientos_de_otro_caso_no_se_mezclan()
    {
        var otro = CasoDeAna();

        Assert.Throws<ExcepcionDeDominio>(() => ContenidoPericial.Armar(otro, _evidencias, _cadena));
    }

    [Fact]
    public void Una_cadena_vacia_no_da_informe()
    {
        Assert.Throws<ExcepcionDeDominio>(() => ContenidoPericial.Armar(_caso, [], []));
    }
}
