#!/usr/bin/env bash
# Pasa los compose de producción (despliegue/) a las imágenes que publicó y firmó un run del CI en main.
#
#   scripts/actualizar-digests-produccion.sh <id del run de ci.yml en main>
#
# El id sale de `gh run list --workflow ci.yml --branch main`. Toma del log del run las cuatro imágenes
# por digest, las verifica con scripts/verificar-imagenes.sh (firma y SBOM, requiere cosign) y recién
# entonces reescribe los compose. Desplegar es commitear ese cambio en un PR. Requiere gh autenticado.
set -euo pipefail

if [ $# -ne 1 ]; then
    sed -n '2,8p' "$0" | sed 's/^# \{0,1\}//'
    exit 2
fi
run=$1
repositorio=${FORENSIC_REPOSITORIO:-YagoGomez83/forensic-video}
raiz=$(cd "$(dirname "$0")/.." && pwd -P)
imagenes=(servidor worker-ingest worker-procesamiento worker-ia)

# Sólo un push a main del workflow ci.yml que terminó bien: es el único que publica y firma.
IFS=$'\t' read -r evento rama conclusion workflow sha < <(gh run view "$run" --repo "$repositorio" \
    --json event,headBranch,conclusion,workflowName,headSha \
    --jq '[.event, .headBranch, .conclusion, .workflowName, .headSha] | @tsv')
if [ "$evento" != push ] || [ "$rama" != main ] || [ "$conclusion" != success ] || [ "$workflow" != CI ]; then
    echo "El run $run no es un push a main de CI terminado bien ($workflow, $evento, $rama, $conclusion)." >&2
    exit 1
fi

registro="ghcr.io/${repositorio,,}"
log=$(gh run view "$run" --repo "$repositorio" --log)
referencias=()
for nombre in "${imagenes[@]}"; do
    encontradas=$(grep -oE "$registro/$nombre@sha256:[0-9a-f]{64}" <<< "$log" | sort -u)
    if [ "$(grep -c . <<< "$encontradas")" -ne 1 ]; then
        echo "El log del run $run no tiene exactamente una referencia de $nombre." >&2
        exit 1
    fi
    referencias+=("$encontradas")
done

"$raiz/scripts/verificar-imagenes.sh" "${referencias[@]}"

for referencia in "${referencias[@]}"; do
    nombre=${referencia#"$registro/"}
    nombre=${nombre%@*}
    sed -i -E "s#$registro/$nombre:sha-[0-9a-f]+@sha256:[0-9a-f]{64}#$registro/$nombre:sha-${sha::12}@${referencia#*@}#" \
        "$raiz"/despliegue/*/compose.yml
done
echo "Compose de producción en las imágenes de ${sha::12} (run $run):"
git -C "$raiz" diff --stat -- despliegue
