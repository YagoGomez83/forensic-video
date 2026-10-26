# Worker de IA (sólo investigación)

Corre en la workstation del analista y sólo lo llama el cliente local. Todo lo que produce es
«sólo investigación»: el servidor lo asienta con la procedencia que este worker declara (modelo,
hash de los pesos, runtime, proveedor y GPU), nunca como prueba.

- Pesos clavados en la imagen: se bajan al construirla con `scripts/descargar_modelos.py`, que los
  verifica contra el SHA-256 de `app/modelos.json` (si no coincide, el build falla), y el worker los
  vuelve a verificar al arrancar. Un peso ausente, ilegible o alterado = el worker no arranca. Nada se
  descarga en caliente. El Dockerfile no usa sintaxis de BuildKit: construye igual con el builder
  clásico (Testcontainers).
- `restricciones.txt`: versiones exactas de todo lo que entra en la imagen, CUDA y cuDNN incluidos.
- ONNX Runtime usa CUDA si la ve y si no cae a CPU con los mismos pesos. El proveedor efectivo va en
  la procedencia.

## API

- `GET /v1/entorno`: modelos verificados (hash, licencia, proveedor efectivo) y entorno.
- `POST /v1/detectar` (multipart: `parametros` = `{"umbralConfianzaMilesimas": 1..999}`, `entrada` =
  la imagen en crudo): detección con YOLOX-s sobre las 80 clases de COCO. Devuelve el SHA-256 de los
  bytes recibidos, el modelo, el entorno (con el proveedor y las constantes del postproceso) y los
  hallazgos: región (envolvente entera de la caja, recortada a la imagen), clase, confianza y el dato
  crudo del modelo (caja en flotante sin recortar, objetividad, puntaje de clase). Cero hallazgos
  también es un resultado. Misma lectura de imagen que el worker de procesamiento: sin orientación
  EXIF, una sola página.

- `POST /v1/superresolucion` (multipart: `parametros` = `{}`, `entrada` = la imagen en crudo, hasta
  1920 x 1080): Real-ESRGAN x4plus en mosaicos de 128 con margen de 10. Devuelve el derivado PNG
  (compresión fija) y en la cabecera `X-Reporte` su procedencia: operación, modelo, SHA-256 de la
  entrada y de la salida, entorno. El cliente reenvía PNG y reporte al servidor tal cual. Inventa
  detalle plausible, no recupera el real.

Son pistas, no conclusiones: «el modelo vio un auto con 0,82», nunca «hay un auto».

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
