# Instala el cliente de escritorio en una workstation, desde los archivos de un release cliente-v*
# (docs/cliente-instalacion.md). Se corre como administrador, en la carpeta donde se bajó el release:
#
#   powershell -ExecutionPolicy Bypass -File .\instalar-cliente.ps1 [-ExigirFirma]
#
# 1. Comprueba forensic-cliente-win-x64.zip contra SHA256SUMS y, si hay cosign (o con -ExigirFirma),
#    que SHA256SUMS lo firmó release-cliente.yml de este repositorio.
# 2. Lo descomprime en «Program Files\ForensicVideo\Cliente\<versión>»: las versiones conviven, y volver
#    a una anterior es apuntar el acceso directo a su carpeta.
# 3. Crea (o actualiza) el acceso directo «Forensic Video» del menú Inicio, para todos los usuarios.
# 4. Si no existe, crea ProgramData\ForensicVideo\cliente.json desde el ejemplo, escribible sólo por
#    administradores; si existe, no lo toca.
# El final lo hace cada analista con su usuario (el certificado de la workstation está en su almacén
# personal): «Forensic.Cliente.exe --comprobar».
param(
    [string]$Carpeta = $PSScriptRoot,
    [switch]$ExigirFirma
)
$ErrorActionPreference = 'Stop'

$identidad = '^https://github\.com/YagoGomez83/forensic-video/\.github/workflows/release-cliente\.yml@refs/tags/cliente-v'
$emisor = 'https://token.actions.githubusercontent.com'
$zip = Join-Path $Carpeta 'forensic-cliente-win-x64.zip'
$sumas = Join-Path $Carpeta 'SHA256SUMS'
$destinoBase = Join-Path $env:ProgramFiles 'ForensicVideo\Cliente'
$carpetaConfig = Join-Path $env:ProgramData 'ForensicVideo'
$config = Join-Path $carpetaConfig 'cliente.json'

$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Hay que correrlo como administrador: instala en Program Files y escribe la configuración de la máquina.'
}

# 1. Integridad y procedencia.
$linea = @(Get-Content $sumas | Where-Object { $_ -match '^[0-9a-f]{64} [ *]forensic-cliente-win-x64\.zip$' })
if ($linea.Count -ne 1) { throw "$sumas no tiene una línea para forensic-cliente-win-x64.zip." }
$esperado = $linea[0].Substring(0, 64)
$real = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
if ($real -ne $esperado) { throw "El zip no es el del release: SHA-256 $real, SHA256SUMS dice $esperado." }
Write-Output "[OK] SHA-256 del zip: $real"

if (Get-Command cosign -ErrorAction SilentlyContinue) {
    cosign verify-blob --bundle (Join-Path $Carpeta 'SHA256SUMS.sigstore.json') `
        --certificate-identity-regexp $identidad --certificate-oidc-issuer $emisor $sumas
    if ($LASTEXITCODE -ne 0) { throw 'La firma de SHA256SUMS no es la de release-cliente.yml de este repositorio.' }
    Write-Output '[OK] SHA256SUMS lo firmó release-cliente.yml.'
}
elseif ($ExigirFirma) {
    throw 'Con -ExigirFirma hace falta cosign (https://docs.sigstore.dev) en el PATH.'
}
else {
    Write-Warning ('Sin cosign sólo se comprobó el hash contra SHA256SUMS, que vino en la misma descarga: ' +
        'eso detecta un zip dañado, no uno reemplazado junto con SHA256SUMS. Ver docs/cliente-instalacion.md.')
}

# 2. Descomprimir en una carpeta por versión. La versión es la que el ejecutable dice de sí mismo.
New-Item -ItemType Directory -Force $destinoBase | Out-Null
$provisoria = Join-Path $destinoBase ".instalando-$([guid]::NewGuid())"
try {
    Expand-Archive $zip $provisoria
    $exe = Join-Path $provisoria 'Forensic.Cliente.exe'
    $version = (Get-Item $exe).VersionInfo.ProductVersion
    if ($version -notmatch '^v\d+\.\d+\.\d+-[0-9a-f]{12}$') { throw "Versión inesperada en el ejecutable: «$version»." }
    $destino = Join-Path $destinoBase $version
    if (Test-Path $destino) { throw "La versión $version ya está instalada en $destino." }
    # El hash ya se comprobó: se quita la marca de «descargado de internet» que Expand-Archive copia a
    # cada archivo, para que SmartScreen no frene un ejecutable verificado.
    Get-ChildItem $provisoria -Recurse -File | Unblock-File
    Move-Item $provisoria $destino
}
finally {
    if (Test-Path $provisoria) { Remove-Item -Recurse -Force $provisoria }
}
$exe = Join-Path $destino 'Forensic.Cliente.exe'
Write-Output "[OK] Forensic Video $version en $destino"

# 3. Acceso directo para todos los usuarios.
$accesos = [Environment]::GetFolderPath('CommonPrograms')
$shell = New-Object -ComObject WScript.Shell
$acceso = $shell.CreateShortcut((Join-Path $accesos 'Forensic Video.lnk'))
$acceso.TargetPath = $exe
$acceso.WorkingDirectory = $destino
$acceso.Description = "Forensic Video $version"
$acceso.Save()
Write-Output '[OK] Acceso directo «Forensic Video» en el menú Inicio.'

# 4. Configuración de la workstation: la escriben sólo los administradores.
if (Test-Path $config) {
    Write-Output "La configuración $config ya existe: no se toca."
}
else {
    New-Item -ItemType Directory -Force $carpetaConfig | Out-Null
    $acl = New-Object Security.AccessControl.DirectorySecurity
    $acl.SetAccessRuleProtection($true, $false)
    $herencia = 'ContainerInherit, ObjectInherit'
    foreach ($regla in @(
            @('S-1-5-32-544', 'FullControl'),       # Administradores
            @('S-1-5-18', 'FullControl'),           # SYSTEM
            @('S-1-5-32-545', 'ReadAndExecute'))) { # Usuarios
        $sid = New-Object Security.Principal.SecurityIdentifier($regla[0])
        $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($sid, $regla[1], $herencia, 'None', 'Allow')))
    }
    Set-Acl $carpetaConfig $acl
    Copy-Item (Join-Path $Carpeta 'cliente.json.example') $config
    Write-Output "Se creó $config desde el ejemplo (sólo los administradores pueden modificarlo)."
    Write-Output "Completalo y copiá ca.crt a $carpetaConfig."
}
# --comprobar va con el usuario de cada analista: el certificado de la workstation está en su almacén
# personal, no en el del administrador.
Write-Output "Después, cada analista con su usuario: & '$exe' --comprobar"
