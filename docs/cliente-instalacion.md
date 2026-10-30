# Instalar el cliente de escritorio en una workstation

El cliente de escritorio se publica como release `cliente-v*` del repositorio (https://github.com/YagoGomez83/forensic-video/releases). Es una carpeta autocontenida para Windows x64: trae el runtime de .NET, así que en la workstation no hace falta instalar el SDK ni nada más. El worker de IA de la workstation se instala aparte, con `despliegue/workstation/compose.yml`.

## 1. Lo que hace falta

- Windows 10 u 11 de 64 bits y una cuenta de administrador.
- De la carpeta de la CA del laboratorio (`scripts/generar-certificados-mtls.sh`, fuera del repo): `ca.crt`, el `.pfx` de esta workstation y su huella SHA-1, que el script imprime al emitirlo.
- La dirección del servidor de custodia, con el nombre que figura en su certificado (por ejemplo `https://custodia.lab:8443/`).

## 2. Bajar el release y comprobarlo

Del release, bajar a una misma carpeta: `forensic-cliente-win-x64.zip`, `instalar-cliente.ps1`, `cliente.json.example`, `SHA256SUMS` y `SHA256SUMS.sigstore.json`.

`SHA256SUMS` viene firmado sin claves (Sigstore) por el workflow de release del repositorio. Con [cosign](https://docs.sigstore.dev) en el PATH se comprueba que lo produjo ese workflow para ese release (reemplace `cliente-v1.0.0` por el release que bajó):

```
cosign verify-blob --bundle SHA256SUMS.sigstore.json `
  --certificate-identity https://github.com/YagoGomez83/forensic-video/.github/workflows/release-cliente.yml@refs/tags/cliente-v1.0.0 `
  --certificate-oidc-issuer https://token.actions.githubusercontent.com SHA256SUMS
```

Después, que el instalador sea el publicado: `Get-FileHash .\instalar-cliente.ps1` tiene que coincidir con su línea de `SHA256SUMS`. El instalador comprueba el zip por su cuenta.

Sin cosign, el instalador sólo puede comparar el zip con `SHA256SUMS`, que vino en la misma descarga: eso detecta un archivo dañado, no uno reemplazado junto con `SHA256SUMS`. En ese caso, compare `SHA256SUMS` con el que muestran las notas del release en GitHub.

## 3. Instalar

En PowerShell **como administrador**, en la carpeta de la descarga:

```
powershell -ExecutionPolicy Bypass -File .\instalar-cliente.ps1
```

`-ExigirFirma` hace que falle si no hay cosign. El instalador:

1. Comprueba el zip contra `SHA256SUMS` y, si hay cosign, la firma de `SHA256SUMS`.
2. Lo descomprime en `C:\Program Files\ForensicVideo\Cliente\<versión>\`. Nunca pisa una versión ya instalada: las versiones conviven.
3. Les quita a los archivos la marca de «descargado de internet», porque ya están verificados.
4. Crea el acceso directo **Forensic Video** del menú Inicio para todos los usuarios, apuntando a esta versión.
5. Si no existe, crea `C:\ProgramData\ForensicVideo\cliente.json` desde el ejemplo. La carpeta queda escribible sólo por Administradores y SYSTEM; los usuarios sólo la leen. Si ya existe, no la toca.

## 4. Configurar la workstation

Copie `ca.crt` a `C:\ProgramData\ForensicVideo\` y complete `cliente.json`:

```json
{
  "servidor": "https://custodia.lab:8443/",
  "caServidor": "C:\\ProgramData\\ForensicVideo\\ca.crt",
  "certificadoCliente": "0123456789abcdef0123456789abcdef01234567",
  "workerIa": "http://127.0.0.1:8100/"
}
```

| Clave | Qué es |
|---|---|
| `servidor` | **Obligatoria.** URL del servidor de custodia. Fuera de esta máquina tiene que ser `https`: el canal es TLS mutuo. |
| `caServidor` | Con `https`, obligatoria. Ruta a la CA del laboratorio en PEM. El cliente sólo acepta un servidor con un certificado de esta CA y con el nombre de la URL. |
| `certificadoCliente` | Con `https`, obligatoria. La huella SHA-1 (40 hex) del certificado de esta workstation en el almacén personal del analista, o la ruta a un certificado PEM. |
| `claveCliente` | Sólo con un certificado PEM: la ruta a su clave privada. Con la huella no se usa; es lo recomendado, porque así la clave no sale del almacén de Windows. |
| `workerIa` | URL del worker de IA de esta workstation. Por defecto `http://127.0.0.1:8100/`. Sólo se acepta loopback. |

Una clave desconocida, por ejemplo mal escrita, es un error, no se ignora. **Si este archivo existe, el cliente no mira las variables de entorno `FORENSIC_*`**: una variable de usuario no puede apuntar la workstation a otro servidor ni a otra CA.

## 5. El certificado de la workstation

Cada analista que use la workstation importa el `.pfx` **con su propio usuario**: doble clic en el archivo, ubicación **Usuario actual**, almacén **Personal**. No marque la clave como exportable.

El cliente lo busca por la huella en el almacén personal del usuario que lo abre. Si un certificado se revoca (`scripts/revocar-certificado-mtls.sh`), el servidor rechaza esa workstation en la siguiente conexión.

## 6. Comprobar

Cada analista, con su usuario:

```
& 'C:\Program Files\ForensicVideo\Cliente\<versión>\Forensic.Cliente.exe' --comprobar
```

Imprime la versión, de dónde salió la configuración, el servidor y el worker de IA, y hace una petición al servidor sin abrir la ventana. Cualquier respuesta del servidor, aunque sea «sin sesión», prueba que el TLS mutuo funciona en los dos sentidos.

| Código de salida | Significa |
|---|---|
| 0 | Configuración correcta y el servidor respondió. |
| 1 | La configuración no sirve, o la CA o el certificado no cargan. El mensaje dice qué y dónde. |
| 2 | La configuración está bien pero el servidor no respondió: red, servidor apagado, o rechazo TLS (certificado revocado, de otra CA o con otro nombre). El mensaje trae la causa. |

Si al abrirlo normalmente la configuración no sirve, el programa muestra una ventana con el mismo motivo en lugar de la pantalla de login.

## 7. Actualizar, volver atrás, desinstalar

- **Actualizar:** se instala el release nuevo igual que el primero. La versión anterior queda en su carpeta y el acceso directo pasa a la nueva. La configuración no cambia.
- **Volver a una versión anterior:** apunte el acceso directo (`C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Forensic Video.lnk`) a la carpeta de esa versión.
- **Desinstalar:** borre `C:\Program Files\ForensicVideo\Cliente\`, el acceso directo y, si no queda nada que conectar, `C:\ProgramData\ForensicVideo\`.

La versión que corre cada workstation se ve en el título de la ventana (por ejemplo `Forensic Video v1.0.0-0123456789ab`: el release y el commit del que salió).

## 8. Limitaciones

- **Sin firma Authenticode.** La procedencia se prueba con la firma Sigstore de `SHA256SUMS`, que Windows no mira. Si se copia la carpeta sin pasar por el instalador, SmartScreen puede avisar «editor desconocido».
- **Sólo Windows x64.** Las workstations son Windows con GPU NVIDIA.
- **Sin autoactualización:** la versión cambia sólo cuando un administrador instala un release.

## Desarrollo

Sin `cliente.json` en ProgramData, el cliente toma la configuración de las variables de entorno: `FORENSIC_SERVIDOR` (por defecto `http://localhost:5065/`), `FORENSIC_CA_SERVIDOR`, `FORENSIC_CERTIFICADO_CLIENTE`, `FORENSIC_CLAVE_CLIENTE` y `FORENSIC_WORKER_IA`. El zip se arma con `scripts/empaquetar-cliente.ps1 -Version vX.Y.Z`, el mismo script que usan el CI y el release.
