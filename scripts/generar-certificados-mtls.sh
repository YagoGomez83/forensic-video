#!/usr/bin/env bash
# Genera la CA del laboratorio y los certificados de mTLS entre las workstations y el servidor.
#
#   scripts/generar-certificados-mtls.sh <carpeta fuera del repo> <host del servidor> [workstation ...]
#
# Deja en la carpeta:
#   ca.crt / ca.key                CA del laboratorio. ca.key firma todo: guardala offline, nunca en el servidor.
#   servidor.crt / servidor.key    para Tls:CertificadoServidor / Tls:ClaveServidor (Docker secrets).
#   <workstation>.crt / .key / .pfx  uno por workstation; el .pfx es para importarlo en el almacén de Windows.
#
# Si ca.key ya existe se reusa: así se agregan workstations sin reemitir todo. Para el .pfx se pide una
# contraseña, o se toma de la variable PFX_CLAVE.
set -euo pipefail

if [ $# -lt 2 ]; then
    sed -n '2,13p' "$0" | sed 's/^# \{0,1\}//'
    exit 2
fi
destino=$1
host=$2
shift 2
workstations=("$@")
[ ${#workstations[@]} -eq 0 ] && workstations=(workstation-01)

# Nada de claves dentro del repo.
repo=$(cd "$(dirname "$0")/.." && pwd -P)
mkdir -p "$destino"
destino=$(cd "$destino" && pwd -P)
case "$destino/" in
    "$repo"/*) echo "La carpeta $destino está dentro del repo: elegí una fuera de él." >&2; exit 1 ;;
esac

umask 077
cd "$destino"
# En Git Bash (Windows), que no convierta "/CN=…" en una ruta.
export MSYS_NO_PATHCONV=1
# Un OPENSSL_CONF que apunta a un archivo inexistente (lo dejan algunos instaladores) rompe openssl req.
if [ -n "${OPENSSL_CONF:-}" ] && [ ! -f "$OPENSSL_CONF" ]; then unset OPENSSL_CONF; fi
# En la carpeta de destino, no en /tmp: un openssl nativo de Windows no ve las rutas de MSYS.
extensiones=.extensiones
trap 'rm -f "$extensiones"' EXIT

clave_ec() { openssl genpkey -algorithm EC -pkeyopt ec_paramgen_curve:P-256 -out "$1" 2>/dev/null; }

emitir() { # <nombre> <CN> <extendedKeyUsage> [subjectAltName]
    local nombre=$1 cn=$2 uso=$3 san=${4:-}
    clave_ec "$nombre.key"
    {
        echo "basicConstraints=critical,CA:FALSE"
        echo "keyUsage=critical,digitalSignature"
        echo "extendedKeyUsage=$uso"
        [ -n "$san" ] && echo "subjectAltName=$san"
    } > "$extensiones"
    openssl req -new -key "$nombre.key" -subj "/CN=$cn" -out "$nombre.csr"
    openssl x509 -req -in "$nombre.csr" -CA ca.crt -CAkey ca.key -CAcreateserial -sha256 -days 825 \
        -extfile "$extensiones" -out "$nombre.crt" 2>/dev/null
    rm -f "$nombre.csr"
}

if [ ! -f ca.key ]; then
    clave_ec ca.key
    openssl req -x509 -new -key ca.key -sha256 -days 3650 -subj "/CN=CA del laboratorio forense" \
        -addext "basicConstraints=critical,CA:TRUE,pathlen:0" -addext "keyUsage=critical,keyCertSign,cRLSign" -out ca.crt
    echo "CA nueva: ca.crt (ca.key: guardala offline)."
fi

san="DNS:$host"
[[ $host =~ ^[0-9.]+$ ]] && san="IP:$host"
emitir servidor "$host" serverAuth "$san"
echo "Servidor: servidor.crt / servidor.key para $host."

for ws in "${workstations[@]}"; do
    emitir "$ws" "$ws" clientAuth
    if [ -n "${PFX_CLAVE:-}" ]; then
        openssl pkcs12 -export -in "$ws.crt" -inkey "$ws.key" -name "$ws" -out "$ws.pfx" -passout env:PFX_CLAVE
    else
        openssl pkcs12 -export -in "$ws.crt" -inkey "$ws.key" -name "$ws" -out "$ws.pfx"
    fi
    huella=$(openssl x509 -in "$ws.crt" -noout -fingerprint -sha1 | sed 's/.*=//; s/://g')
    echo "Workstation $ws: $ws.pfx; huella para FORENSIC_CERTIFICADO_CLIENTE: $huella"
done
