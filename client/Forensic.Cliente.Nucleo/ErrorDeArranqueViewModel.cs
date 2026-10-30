namespace Forensic.Cliente.Nucleo;

/// <summary>
/// El programa no pudo arrancar: la configuración de la workstation no sirve o su certificado no carga.
/// Se muestra en lugar de la ventana principal, con el motivo y dónde corregirlo.
/// </summary>
/// <param name="motivo">Qué falló, tal como lo dijo la validación.</param>
/// <param name="rutaDeConfiguracion">El archivo de configuración de la workstation.</param>
public sealed class ErrorDeArranqueViewModel(string motivo, string rutaDeConfiguracion)
{
    public string Motivo { get; } = motivo;

    public string Indicacion { get; } =
        $"La configuración de esta workstation está en {rutaDeConfiguracion} (o, si ese archivo no existe, en las " +
        "variables de entorno FORENSIC_*). La corrige un administrador; después, vuelva a abrir el programa.";
}
