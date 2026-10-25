using System.Text.Json.Serialization;
using Forensic.Api;
using Forensic.Api.Controllers;
using Forensic.Api.Identidad;
using Forensic.Application;
using Forensic.Infrastructure;
using Forensic.Infrastructure.Persistencia;
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
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Configuration.GetValue<bool>("Persistencia:MigrarAlIniciar"))
{
    using var scope = app.Services.CreateScope();
    await Forensic.Infrastructure.DependencyInjection.MigrarAsync(scope.ServiceProvider);
}

app.MapControllers();

app.Run();

public partial class Program;
