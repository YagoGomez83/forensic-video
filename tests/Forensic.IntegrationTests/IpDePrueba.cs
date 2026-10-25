using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Forensic.IntegrationTests;

/// <summary>
/// SÓLO de tests: TestServer no tiene IP remota, así que esta cabecera la simula para probar el
/// límite por IP. Corre antes que todo el pipeline del servidor. El servidor real no la conoce.
/// </summary>
internal sealed class IpDePrueba : IStartupFilter
{
    public const string Cabecera = "X-Ip-De-Prueba";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> siguiente) => app =>
    {
        app.Use((contexto, resto) =>
        {
            if (IPAddress.TryParse(contexto.Request.Headers[Cabecera], out var ip))
                contexto.Connection.RemoteIpAddress = ip;
            return resto(contexto);
        });
        siguiente(app);
    };
}
