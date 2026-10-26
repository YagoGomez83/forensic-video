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
}
