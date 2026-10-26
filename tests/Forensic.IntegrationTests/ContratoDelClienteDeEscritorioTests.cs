using System.Net.Http.Json;
using Forensic.Api.Controllers;
using Forensic.Cliente.Api;
using Forensic.Domain.Comun;

namespace Forensic.IntegrationTests;

/// <summary>
/// El cliente tipado del escritorio (<c>client/Forensic.Cliente.Api</c>) contra el servidor real. El
/// cliente define sus propios contratos a propósito; estos tests detectan cualquier desfasaje.
/// </summary>
public class ContratoDelClienteDeEscritorioTests(ServidorForense servidor) : IClassFixture<ServidorForense>
{
    private ClienteForense Cliente(ISesion? sesion = null)
    {
        servidor.CreateClient().Dispose(); // arranca el servidor de pruebas
        return ClienteForense.Crear(servidor.Server.BaseAddress, sesion ?? new SesionEnMemoria(), servidor.Server.CreateHandler());
    }

    private async Task<ClienteForense> AnalistaAsync(string nombre)
    {
        await servidor.UsuarioConSesionAsync(nombre, Rol.Analista, "la frase de la analista");
        var cliente = Cliente();
        await cliente.IniciarSesionAsync(nombre, "la frase de la analista", CancellationToken.None);
        return cliente;
    }

    [Fact]
    public async Task El_login_y_la_lista_de_casos_del_cliente_coinciden_con_el_servidor()
    {
        var nombre = $"cli-{Guid.NewGuid():N}"[..16];
        var cliente = await AnalistaAsync(nombre);
        var http = servidor.ClienteDe(nombre, Rol.Analista);
        var caso = await http.AbrirCasoAsync();

        var casos = await cliente.ListarCasosAsync(CancellationToken.None);

        var visto = Assert.Single(casos, c => c.Id == caso.Id);
        Assert.Equal((caso.NumeroExpediente, EstadoCaso.Abierto, nombre), (visto.NumeroExpediente, visto.Estado, visto.AbiertoPor));
        Assert.Equal([nombre], visto.AnalistasAsignados);
    }

    [Fact]
    public async Task Las_credenciales_malas_dan_el_error_generico_del_servidor()
    {
        var ex = await Assert.ThrowsAsync<ErrorDeApi>(async () => await Cliente().IniciarSesionAsync("nadie", "una contrasena cualquiera", CancellationToken.None));

        Assert.Equal(TipoDeError.CredencialesInvalidas, ex.Tipo);
        Assert.Equal("Usuario o contraseña incorrectos.", ex.Message);
    }

    [Fact]
    public async Task Un_token_que_el_servidor_ya_no_acepta_expira_la_sesion()
    {
        var sesion = new SesionEnMemoria();
        sesion.Iniciar("no-es-un-jwt", DateTimeOffset.UtcNow.AddMinutes(15));
        var expirada = false;
        sesion.Expirada += (_, _) => expirada = true;

        await Assert.ThrowsAsync<SesionExpiradaException>(() => Cliente(sesion).ListarCasosAsync(CancellationToken.None));

        Assert.True(expirada);
        Assert.False(sesion.Activa);
    }

    [Fact]
    public async Task Con_una_contrasena_temporal_el_cliente_detecta_el_cambio_obligatorio_y_tras_cambiarla_queda_listo()
    {
        var nombre = $"tmp-{Guid.NewGuid():N}"[..16];
        var admin = await servidor.AdminAsync();
        (await admin.PostAsJsonAsync("/usuarios", new CrearUsuarioRequest(nombre, Rol.Analista, "contrasena temporal 2026"), ServidorForense.Json))
            .EnsureSuccessStatusCode();
        var cliente = Cliente();

        Assert.Equal(EstadoDeInicio.DebeCambiarContrasena, await cliente.IniciarSesionAsync(nombre, "contrasena temporal 2026", CancellationToken.None));
        await cliente.CambiarContrasenaAsync("contrasena temporal 2026", "la frase definitiva", CancellationToken.None);

        // Con la contraseña cambiada la sesión ya es completa: listar casos no da 403.
        await cliente.ListarCasosAsync(CancellationToken.None);
        Assert.Equal(EstadoDeInicio.Listo, await Cliente().IniciarSesionAsync(nombre, "la frase definitiva", CancellationToken.None));
    }

    [Fact]
    public async Task El_flujo_del_analista_de_punta_a_punta_con_el_cliente_tipado()
    {
        var cliente = await AnalistaAsync($"flujo-{Guid.NewGuid():N}"[..16]);
        var ct = CancellationToken.None;
        var caso = await cliente.AbrirCasoAsync($"EXP-{Guid.NewGuid():N}"[..20], "Flujo del escritorio", ct);
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(200_000);
        var enviados = new List<long>();

        var original = await cliente.IngestarAsync(caso.Id, "cámara 1.png", new MemoryStream(bytes), bytes.Length, enviados.Add, ct);
        Assert.Equal(HashSha256.Calcular(bytes).Hex, original.Hash);
        Assert.Equal(bytes.Length, enviados[^1]);
        Assert.Equal(bytes, await cliente.DescargarContenidoAsync(caso.Id, original.Id, ct));

        var aplicada = await cliente.AplicarOperacionAsync(caso.Id, original.Id, "opencv.recorte/v1", new { x = 5, y = 6, ancho = 16, alto = 9 }, null, ct);
        Assert.Equal("""{"alto":9,"ancho":16,"x":5,"y":6}""", aplicada.Parametros);
        Assert.Equal((original.Id, Forensic.Cliente.Api.ViaProcesamiento.AdmisibleEnJuicio), (aplicada.Derivado.PadreId, aplicada.Derivado.Via));
        Assert.Equal(["cámara 1.png", "cámara 1-recorte.png"], (await cliente.ListarEvidenciasAsync(caso.Id, ct)).Select(e => e.NombreOriginal));

        var reproduccion = await cliente.ReproducirAsync(caso.Id, aplicada.Derivado.Id, ct);
        Assert.True(reproduccion.Reproducido);
        Assert.Equal(aplicada.Derivado.Hash, reproduccion.Sha256Obtenido);

        var linaje = await cliente.ObtenerLinajeAsync(caso.Id, aplicada.Derivado.Id, ct);
        Assert.Equal([original.Id, aplicada.Derivado.Id], linaje.Select(e => e.Evidencia.Id));
        Assert.Equal("opencv.recorte/v1", linaje[1].Operacion!.Tipo);
        Assert.True(Assert.Single(linaje[1].Reproducciones).Reproducido);

        var custodia = await cliente.ExportarCustodiaAsync(caso.Id, ct);
        Assert.True(custodia.Verificacion.EsIntegra, custodia.Verificacion.Motivo);
        Assert.Equal(
            ["CasoAbierto", "EvidenciaIngestada", "EvidenciaAccedida", "OperacionAplicada", "OperacionReproducida"],
            custodia.Asientos.Select(a => a.Tipo));
    }
}
