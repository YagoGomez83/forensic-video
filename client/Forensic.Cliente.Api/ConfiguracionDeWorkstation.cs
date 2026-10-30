using System.Text.Json;

namespace Forensic.Cliente.Api;

/// <summary>
/// Cómo se conecta esta workstation: el servidor de custodia, la CA del laboratorio, su certificado de
/// cliente y su worker de IA. La escribe el admin en <see cref="RutaPorDefecto"/> (sólo lectura para el
/// analista); si ese archivo existe, manda él y las variables de entorno no cuentan, así que una variable
/// de usuario no puede apuntar la workstation a otro servidor. Sin archivo (desarrollo), las variables.
/// </summary>
public sealed record ConfiguracionDeWorkstation(
    Uri Servidor, string? CaServidor, string? CertificadoCliente, string? ClaveCliente, Uri WorkerIa, string Origen)
{
    /// <summary>URL del servidor de custodia.</summary>
    public const string VariableDelServidor = "FORENSIC_SERVIDOR";

    /// <summary>Ruta a la CA del laboratorio en PEM.</summary>
    public const string VariableDeLaCa = "FORENSIC_CA_SERVIDOR";

    /// <summary>
    /// El certificado de esta workstation: su huella (SHA-1, 40 hex) en el almacén personal del usuario
    /// (recomendado en Windows: la clave no sale del almacén), o la ruta a un certificado PEM.
    /// </summary>
    public const string VariableDelCertificado = "FORENSIC_CERTIFICADO_CLIENTE";

    /// <summary>Si el certificado es una ruta PEM, la ruta a su clave privada PEM.</summary>
    public const string VariableDeLaClave = "FORENSIC_CLAVE_CLIENTE";

    /// <summary>URL del worker de IA de esta workstation. Sólo se acepta loopback.</summary>
    public const string VariableDelWorkerIa = "FORENSIC_WORKER_IA";

    public static readonly string RutaPorDefecto = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ForensicVideo", "cliente.json");

    private static readonly Uri ServidorDeDesarrollo = new("http://localhost:5065/");

    // Clave del archivo → variable equivalente. El orden es el del mensaje de error.
    private static readonly (string Clave, string Variable)[] Campos =
    [
        ("servidor", VariableDelServidor),
        ("caServidor", VariableDeLaCa),
        ("certificadoCliente", VariableDelCertificado),
        ("claveCliente", VariableDeLaClave),
        ("workerIa", VariableDelWorkerIa),
    ];

    /// <summary>El certificado está en el almacén personal (huella SHA-1) y no en un PEM.</summary>
    public bool CertificadoEnAlmacen =>
        CertificadoCliente is { Length: 40 } huella && huella.All(Uri.IsHexDigit);

    public static ConfiguracionDeWorkstation Cargar(string rutaArchivo, Func<string, string?> variable) =>
        File.Exists(rutaArchivo) ? DesdeArchivo(rutaArchivo) : DesdeEntorno(variable);

    private static ConfiguracionDeWorkstation DesdeArchivo(string ruta)
    {
        var valores = LeerArchivo(ruta);
        string Nombre(string clave) => $"«{clave}» en {ruta}";
        if (!valores.ContainsKey("servidor"))
            throw new ConfiguracionInvalidaException($"Falta {Nombre("servidor")}: la URL del servidor de custodia.");
        return Validar(clave => valores.GetValueOrDefault(clave), Nombre, ruta);
    }

    private static ConfiguracionDeWorkstation DesdeEntorno(Func<string, string?> variable)
    {
        string VariableDe(string clave) => Campos.Single(c => c.Clave == clave).Variable;
        return Validar(clave => variable(VariableDe(clave)), VariableDe, "variables de entorno");
    }

    private static Dictionary<string, string> LeerArchivo(string ruta)
    {
        JsonDocument documento;
        try
        {
            documento = JsonDocument.Parse(File.ReadAllText(ruta));
        }
        catch (JsonException ex)
        {
            throw new ConfiguracionInvalidaException($"{ruta} no es JSON válido: {ex.Message}");
        }
        using (documento)
        {
            if (documento.RootElement.ValueKind != JsonValueKind.Object)
                throw new ConfiguracionInvalidaException($"{ruta} tiene que ser un objeto JSON.");
            var valores = new Dictionary<string, string>();
            foreach (var propiedad in documento.RootElement.EnumerateObject())
            {
                // Una clave mal escrita dejaría su valor sin efecto sin que nadie lo note.
                if (!Campos.Any(c => c.Clave == propiedad.Name))
                    throw new ConfiguracionInvalidaException(
                        $"{ruta} tiene una clave desconocida, «{propiedad.Name}». Las válidas: {string.Join(", ", Campos.Select(c => c.Clave))}.");
                if (propiedad.Value.ValueKind != JsonValueKind.String)
                    throw new ConfiguracionInvalidaException($"«{propiedad.Name}» en {ruta} tiene que ser un texto.");
                valores[propiedad.Name] = propiedad.Value.GetString()!;
            }
            return valores;
        }
    }

    private static ConfiguracionDeWorkstation Validar(
        Func<string, string?> valor, Func<string, string> nombre, string origen)
    {
        var servidor = valor("servidor") is { } s ? Url(s, nombre("servidor")) : ServidorDeDesarrollo;
        var workerIa = valor("workerIa") is { } w ? Url(w, nombre("workerIa")) : ClienteWorkerIa.PorDefecto;
        var config = new ConfiguracionDeWorkstation(
            servidor, valor("caServidor"), valor("certificadoCliente"), valor("claveCliente"), workerIa, origen);

        try
        {
            CanalSeguro.ExigirCanalSeguro(servidor);
        }
        catch (InvalidOperationException ex)
        {
            throw new ConfiguracionInvalidaException($"{ex.Message} Revisá {nombre("servidor")}.");
        }
        if (!workerIa.IsLoopback)
            throw new ConfiguracionInvalidaException(
                $"El worker de IA corre en esta workstation: {nombre("workerIa")} tiene que ser 127.0.0.1 o localhost, no {workerIa}.");
        if (servidor.Scheme == Uri.UriSchemeHttps)
        {
            if (config.CaServidor is null)
                throw new ConfiguracionInvalidaException($"Con https hace falta la CA del laboratorio: definí {nombre("caServidor")}.");
            if (config.CertificadoCliente is null)
                throw new ConfiguracionInvalidaException(
                    $"Con https hace falta el certificado de esta workstation: definí {nombre("certificadoCliente")}.");
            if (!config.CertificadoEnAlmacen && config.ClaveCliente is null)
                throw new ConfiguracionInvalidaException($"Con un certificado PEM hace falta su clave: definí {nombre("claveCliente")}.");
        }
        return config;
    }

    private static Uri Url(string texto, string nombre) =>
        Uri.TryCreate(texto, UriKind.Absolute, out var url) && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps)
            ? url
            : throw new ConfiguracionInvalidaException($"{nombre} tiene que ser una URL http o https completa, no «{texto}».");
}

/// <summary>La configuración de la workstation no sirve: el mensaje dice qué falta y dónde.</summary>
public sealed class ConfiguracionInvalidaException(string mensaje) : Exception(mensaje);
