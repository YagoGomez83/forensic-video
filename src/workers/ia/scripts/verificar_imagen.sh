#!/usr/bin/env bash
# Prueba de concepto de la imagen del worker de IA, con la red cortada (--network none): si el
# worker intentara bajar un modelo, no arrancaría. Imprime el entorno (proveedor efectivo y GPU).
#
#   scripts/verificar_imagen.sh forensic-worker-ia:dev          # CPU (CI, máquinas sin NVIDIA)
#   scripts/verificar_imagen.sh forensic-worker-ia:dev --gpus   # exige CUDAExecutionProvider
set -euo pipefail

imagen="${1:?Uso: verificar_imagen.sh <imagen> [--gpus]}"
gpus=()
proveedor_exigido=""
if [[ "${2:-}" == "--gpus" ]]; then
  gpus=(--gpus all)
  proveedor_exigido="CUDAExecutionProvider"
fi

nombre="forensic-ia-verificacion-$$"
docker run -d --rm --name "$nombre" --network none "${gpus[@]}" "$imagen" > /dev/null
trap 'docker rm -f "$nombre" > /dev/null 2>&1 || true' EXIT

for _ in $(seq 1 60); do
  if entorno=$(docker exec "$nombre" python -c \
      "import urllib.request;print(urllib.request.urlopen('http://127.0.0.1:8000/v1/entorno').read().decode())" \
      2> /dev/null); then
    echo "$entorno"
    if [[ -n "$proveedor_exigido" && "$entorno" != *"$proveedor_exigido"* ]]; then
      echo "La imagen arrancó, pero sin $proveedor_exigido: no ve la GPU." >&2
      exit 1
    fi
    exit 0
  fi
  if ! docker inspect "$nombre" > /dev/null 2>&1; then
    echo "El worker terminó al arrancar (¿peso ausente o alterado?)." >&2
    exit 1
  fi
  sleep 2
done
echo "El worker no respondió en 120 s." >&2
docker logs "$nombre" >&2 || true
exit 1
