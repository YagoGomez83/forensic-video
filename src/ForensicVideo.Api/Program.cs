using System.Text.Json.Serialization;
using ForensicVideo.Api;
using ForensicVideo.Api.Identidad;
using ForensicVideo.Application;
using ForensicVideo.Infrastructure;
using ForensicVideo.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAplicacion();
builder.Services.AddInfraestructura(builder.Configuration);
builder.Services.AddOptions<OpcionesIngesta>().Bind(builder.Configuration.GetSection(OpcionesIngesta.Seccion));

if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
    throw new InvalidOperationException(
        "El módulo de identidad todavía no existe: el servidor sólo puede arrancar en Development o Testing.");
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IActorActual, ActorDeDesarrollo>();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ManejadorDeErrores>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Configuration.GetValue<bool>("Persistencia:MigrarAlIniciar"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<ContextoForense>().Database.MigrateAsync();
}

app.MapEndpointsForenses();

app.Run();

public partial class Program;
