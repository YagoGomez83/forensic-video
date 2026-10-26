# Worker de IA (sólo investigación)

Corre en la workstation del analista y sólo lo llama el cliente local. Todo lo que produce es
«sólo investigación»: el servidor lo asienta con la procedencia que este worker declara (modelo,
hash de los pesos, runtime, proveedor y GPU), nunca como prueba.

- Pesos clavados en la imagen (`ADD --checksum`), listados en `app/modelos.json` y verificados por
  SHA-256 al arrancar. Un peso ausente, ilegible o alterado = el worker no arranca. Nada se descarga
  en caliente.
- `restricciones.txt`: versiones exactas de todo lo que entra en la imagen, CUDA y cuDNN incluidos.
- ONNX Runtime usa CUDA si la ve y si no cae a CPU con los mismos pesos. El proveedor efectivo va en
  la procedencia.

## Prueba de concepto con GPU

Requiere una GPU NVIDIA, driver ≥ 580 (CUDA 13) y nvidia-container-toolkit.

```bash
docker build -t forensic-worker-ia:dev src/workers/ia
src/workers/ia/scripts/verificar_imagen.sh forensic-worker-ia:dev --gpus
```

Corre el worker con `--network none` y falla si el proveedor no es `CUDAExecutionProvider`. La salida
lista la GPU (`nvidia-smi`) y el proveedor de cada modelo.

## Desarrollo (sin Docker)

```bash
py -3.12 -m venv .venv && .venv/Scripts/python -m pip install -e ".[dev]"
.venv/Scripts/python scripts/descargar_modelos.py   # sólo desarrollo y CI: baja y verifica los pesos
.venv/Scripts/python -m pytest
```
