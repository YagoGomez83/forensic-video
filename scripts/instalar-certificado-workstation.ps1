# Importa el certificado de la workstation (su .pfx) en el almacén de la máquina, para que lo usen todos los
# usuarios de Windows de la PC: la persona se identifica con su login en el servidor, no con su cuenta de
# Windows (docs/cliente-instalacion.md). Se corre como administrador, una vez por PC:
#
#   powershell -ExecutionPolicy Bypass -File .\instalar-certificado-workstation.ps1 -Pfx .\workstation-01.pfx
#
# 1. Importa el .pfx en LocalMachine\My con la clave privada NO exportable.
# 2. Da a la clave privada permiso de sólo lectura al grupo Usuarios (por SID, vale en cualquier idioma):
#    pueden usarla para el TLS mutuo, no copiarla ni cambiarla.
# 3. Imprime la huella, que tiene que coincidir con «certificadoCliente» de cliente.json.
# La contraseña del .pfx se pide por consola, o se pasa con -Clave (SecureString).
param(
    [Parameter(Mandatory)][string]$Pfx,
    [securestring]$Clave
)
$ErrorActionPreference = 'Stop'

$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Hay que correrlo como administrador: escribe en el almacén de certificados de la máquina.'
}
if (-not (Test-Path $Pfx)) { throw "No existe $Pfx." }
if (-not $Clave) { $Clave = Read-Host "Contraseña de $(Split-Path $Pfx -Leaf)" -AsSecureString }

# 1. Sin -Exportable: la clave no se puede volver a sacar del almacén.
$certificado = Import-PfxCertificate -FilePath $Pfx -CertStoreLocation Cert:\LocalMachine\My -Password $Clave
if (-not $certificado.HasPrivateKey) { throw 'El certificado se importó sin clave privada: ¿es el .pfx de la workstation?' }
Write-Output "[OK] $($certificado.Subject) en el almacén de la máquina (clave no exportable)."

# 2. El archivo de la clave privada: CNG (Crypto\Keys) para ECDSA, o el CSP clásico para RSA.
# (PowerShell no distingue mayúsculas en los nombres: no llamarla $clave, que es la contraseña.)
$privada = [Security.Cryptography.X509Certificates.ECDsaCertificateExtensions]::GetECDsaPrivateKey($certificado)
if (-not $privada) { $privada = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($certificado) }
if ($privada -is [Security.Cryptography.ECDsaCng] -or $privada -is [Security.Cryptography.RSACng]) {
    $nombre = $privada.Key.UniqueName
}
else {
    $nombre = $privada.CspKeyContainerInfo.UniqueKeyContainerName
}
$archivo = @(
    (Join-Path $env:ProgramData "Microsoft\Crypto\Keys\$nombre"),
    (Join-Path $env:ProgramData "Microsoft\Crypto\RSA\MachineKeys\$nombre")
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $archivo) { throw "No se encontró el archivo de la clave privada $nombre." }

$acl = Get-Acl $archivo
$usuarios = New-Object Security.Principal.SecurityIdentifier('S-1-5-32-545')
$acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($usuarios, 'Read', 'Allow')))
Set-Acl $archivo $acl
Write-Output '[OK] La clave privada la pueden usar (sólo leer) los usuarios de esta PC.'

# 3. Para contrastar con cliente.json.
Write-Output "Huella (certificadoCliente en cliente.json): $($certificado.Thumbprint)"
Write-Output "Ya podés borrar $Pfx de esta PC. Después, con cualquier usuario: Forensic.Cliente.exe --comprobar"
