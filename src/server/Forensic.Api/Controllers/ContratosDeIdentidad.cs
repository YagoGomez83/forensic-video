using Forensic.Domain.Comun;

namespace Forensic.Api.Controllers;

public sealed record LoginRequest(string Usuario, string Contrasena);

public sealed record CambioDeContrasenaRequest(string Actual, string Nueva);

public sealed record CrearUsuarioRequest(string Usuario, Rol Rol, string ContrasenaInicial);

public sealed record CambiarRolRequest(Rol Rol);

public sealed record ResetearContrasenaRequest(string ContrasenaTemporal);
