# Arma el cliente de escritorio para las workstations: la carpeta publicada (con el runtime de .NET
# incluido, ver client/Forensic.Cliente/Properties/PublishProfiles/win-x64.pubxml) en un zip.
# Lo usan el CI (prueba de humo) y release-cliente.yml; se corre desde un clon de git, en Windows.
#
#   scripts/empaquetar-cliente.ps1 -Version v1.2.3 [-Salida artefactos]
#
# La versión que muestra el programa (título y --comprobar) es «<Version>-<12 del commit>», el mismo
# esquema que el verificador.
param(
    [Parameter(Mandatory)][ValidatePattern('^v\d+\.\d+\.\d+$')][string]$Version,
    [string]$Salida = 'artefactos'
)
$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$commit = (git -C $repo rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Hace falta un clon de git: la versión lleva el commit.' }
$informacion = "$Version-$($commit.Substring(0, 12))"

$carpeta = Join-Path ([IO.Path]::GetTempPath()) "forensic-cliente-$([guid]::NewGuid())"
try {
    dotnet publish (Join-Path $repo 'client/Forensic.Cliente') `
        -p:PublishProfile=win-x64 `
        -p:PublishDir="$carpeta\" `
        -p:ContinuousIntegrationBuild=true `
        -p:Version=$($Version.Substring(1)) `
        -p:InformationalVersion=$informacion `
        -p:IncludeSourceRevisionInInformationalVersion=false
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish falló ($LASTEXITCODE)." }

    New-Item -ItemType Directory -Force $Salida | Out-Null
    $zip = Join-Path (Resolve-Path $Salida) 'forensic-cliente-win-x64.zip'
    Remove-Item $zip -ErrorAction SilentlyContinue
    Compress-Archive -Path (Join-Path $carpeta '*') -DestinationPath $zip
    Write-Output "Forensic Video $informacion -> $zip"
}
finally {
    Remove-Item -Recurse -Force $carpeta -ErrorAction SilentlyContinue
}
