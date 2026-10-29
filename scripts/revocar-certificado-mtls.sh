#!/usr/bin/env bash
# Revoca el certificado de una workstation y emite la CRL nueva de la CA del laboratorio.
#
#   scripts/revocar-certificado-mtls.sh <carpeta de la CA> [workstation]
#
# Sin workstation sólo re-emite la CRL (hay que hacerlo antes de que venza: dura 365 días y, vencida,
# el servidor no deja entrar a nadie). Corre donde está ca.key, que vive offline; después se copia
# ca.crl al servidor sobre la anterior (Tls:CrlDeClientes) y se aplica en la próxima conexión, sin
# reiniciar. La base de revocaciones de openssl (index.txt) queda en la misma carpeta: es parte de la CA.
set -euo pipefail

if [ $# -lt 1 ] || [ $# -gt 2 ]; then
    sed -n '2,9p' "$0" | sed 's/^# \{0,1\}//'
    exit 2
fi
carpeta=$1
workstation=${2:-}

cd "$carpeta"
if [ ! -f ca.key ] || [ ! -f ca.crt ]; then
    echo "No hay ca.key y ca.crt en $carpeta: la CRL sólo la puede firmar la CA del laboratorio." >&2
    exit 1
fi
umask 077
export MSYS_NO_PATHCONV=1
if [ -n "${OPENSSL_CONF:-}" ] && [ ! -f "$OPENSSL_CONF" ]; then unset OPENSSL_CONF; fi

# La configuración mínima de `openssl ca` para revocar y firmar CRLs, con rutas relativas a la carpeta.
cat > ca.cnf <<'EOF'
[ ca ]
default_ca = laboratorio

[ laboratorio ]
database         = index.txt
crlnumber        = crlnumber
certificate      = ca.crt
private_key      = ca.key
default_md       = sha256
default_crl_days = 365
crl_extensions   = extensiones_crl

[ extensiones_crl ]
authorityKeyIdentifier = keyid:always
EOF
[ -f index.txt ] || : > index.txt
[ -f crlnumber ] || echo 1000 > crlnumber

if [ -n "$workstation" ]; then
    if [ ! -f "$workstation.crt" ]; then
        echo "No existe $workstation.crt en $carpeta." >&2
        exit 1
    fi
    openssl ca -config ca.cnf -revoke "$workstation.crt" 2>/dev/null
    serie=$(openssl x509 -in "$workstation.crt" -noout -serial | sed 's/.*=//')
    echo "Revocado $workstation (serie $serie)."
fi

openssl ca -config ca.cnf -gencrl -out ca.crl 2>/dev/null
revocados=$(grep -c '^R' index.txt || true)
vence=$(openssl crl -in ca.crl -noout -nextupdate | sed 's/.*=//')
echo "CRL nueva: ca.crl ($revocados revocados, vence $vence). Copiala al servidor sobre la anterior."
