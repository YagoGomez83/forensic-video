using System.Text.Json.Serialization;
using Forensic.Api;
using Forensic.Api.Controllers;
using Forensic.Api.Identidad;
using Forensic.Application;
using Forensic.Application.Custodia;
using Forensic.Application.Identidad;
using Forensic.Infrastructure;
using Forensic.Infrastructure.Identidad;
using Microsoft.AspNetCore.Authentication.JwtBearer;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAplicacion();
builder.Services.AddInfraestructura(builder.Configuration);
builder.Services.AddOptions<OpcionesIngesta>().Bind(builder.Configuration.GetSection(OpcionesIngesta.Seccion));
builder.Services.AddOptions<OpcionesDeInformes>().Bind(builder.Configuration.GetSection(OpcionesDeInformes.Seccion));

// Sin clave de firma (o con una corta) el servidor no arranca: ParametrosDeValidacion lanza.
var jwt = builder.Configuration.GetSection(OpcionesJwt.Seccion).Get<OpcionesJwt>() ?? new OpcionesJwt();
var validacion = jwt.ParametrosDeValidacion();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = validacion;
        o.Events = new JwtBearerEvents { OnTokenValidated = ValidacionDeSesion.ValidarAsync };
    });
builder.Services.AddAuthorization(ValidacionDeSesion.Configurar);
LimiteDeLogin.Registrar(builder.Services, builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IActorActual, ActorDelToken>();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ManejadorDeErrores>();
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

// La clave de sellado se carga ya: sin ella (o con una que no es Ed25519) el servidor no arranca.
var firmante = app.Services.GetRequiredService<IFirmanteDeCadena>();
app.Logger.LogInformation("Clave de sellado de la cadena: {ClaveId}", firmante.ClaveId);

app.UseExceptionHandler();
app.UseStatusCodePages();

using (var scope = app.Services.CreateScope())
{
    if (app.Configuration.GetValue<bool>("Persistencia:MigrarAlIniciar"))
        await Forensic.Infrastructure.DependencyInjection.MigrarAsync(scope.ServiceProvider);
    await SembrarAdminInicialAsync(scope.ServiceProvider, app.Configuration, app.Logger);
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

/// <summary>
/// Sin ningún admin activo, el servidor no arranca salvo que la configuración traiga el seed del
/// primero. No hay credenciales por defecto: el admin sembrado debe cambiar su contraseña al entrar.
/// </summary>
static async Task SembrarAdminInicialAsync(IServiceProvider servicios, IConfiguration configuracion, ILogger logger)
{
    var identidad = servicios.GetRequiredService<ServicioDeIdentidad>();
    var usuario = configuracion["Identidad:AdminInicial:Usuario"];
    var contrasena = configuracion["Identidad:AdminInicial:Contrasena"];
    var haySeed = !string.IsNullOrWhiteSpace(usuario) && !string.IsNullOrEmpty(contrasena);

    if (await identidad.ExisteAdminActivoAsync(CancellationToken.None))
    {
        if (haySeed)
            logger.LogWarning("Ya existe un admin activo: se ignora Identidad:AdminInicial. Conviene quitarlo de los secretos.");
        return;
    }
    if (!haySeed)
        throw new InvalidOperationException(
            "No hay ningún admin activo y falta el seed del primero: definí Identidad:AdminInicial:Usuario e " +
            "Identidad:AdminInicial:Contrasena (user-secrets o variables Identidad__AdminInicial__*).");

    await identidad.SembrarAdminInicialAsync(usuario!, contrasena!, CancellationToken.None);
    logger.LogWarning("Se sembró el admin inicial '{Usuario}'; debe cambiar su contraseña en el primer acceso.", usuario);
}

public partial class Program;
