#!/usr/bin/env bash
# Verifica, antes de desplegarlas, que las imágenes las firmó el CI de este repositorio en main.
#
#   scripts/verificar-imagenes.sh ghcr.io/<owner>/<repo>/servidor@sha256:… [otra@sha256:… …]
#
# Por digest, nunca por tag: un tag se puede mover. Comprueba la firma sin claves (Sigstore) contra la
# identidad del workflow .github/workflows/ci.yml en refs/heads/main, y la atestación del SBOM
# (CycloneDX). Requiere cosign (https://docs.sigstore.dev). Sale con 1 si alguna no verifica.
set -euo pipefail

if [ $# -eq 0 ]; then
    sed -n '2,9p' "$0" | sed 's/^# \{0,1\}//'
    exit 2
fi
repositorio=${FORENSIC_REPOSITORIO:-YagoGomez83/forensic-video}
identidad="https://github.com/$repositorio/.github/workflows/ci.yml@refs/heads/main"
emisor="https://token.actions.githubusercontent.com"

fallas=0
for imagen in "$@"; do
    if [[ $imagen != *@sha256:* ]]; then
        echo "RECHAZADA $imagen: hay que verificar por digest (…@sha256:…), no por tag." >&2
        fallas=1
        continue
    fi
    if cosign verify "$imagen" --certificate-identity "$identidad" --certificate-oidc-issuer "$emisor" > /dev/null \
        && cosign verify-attestation "$imagen" --type cyclonedx --certificate-identity "$identidad" \
            --certificate-oidc-issuer "$emisor" > /dev/null; then
        echo "OK        $imagen (firmada por $identidad, con SBOM)"
    else
        echo "FALLA     $imagen: no la firmó el CI de $repositorio en main." >&2
        fallas=1
    fi
done
exit $fallas
