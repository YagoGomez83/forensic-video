# syntax=docker/dockerfile:1.7
# Worker de IA (sólo investigación). Corre en la workstation con GPU NVIDIA (nvidia-container-toolkit)
# y, sin GPU, cae a CPU con los mismos pesos. Los pesos entran AL CONSTRUIR, verificados por hash
# (ADD --checksum); en ejecución nada se descarga: con la red cortada el worker arranca igual.
FROM python:3.12-slim-bookworm

WORKDIR /srv/worker
# Primero todas las dependencias con versión exacta (incluidas CUDA y cuDNN, que trae el extra «gpu»
# de onnxruntime-gpu): es la capa pesada y no se rehace al cambiar el código del worker.
COPY restricciones.txt ./
RUN --mount=type=cache,target=/root/.cache/pip pip install --no-compile -r restricciones.txt
COPY pyproject.toml ./
COPY app ./app
RUN pip install --no-cache-dir --no-deps .

# Un ADD por peso del manifiesto (app/modelos.json). Sólo lectura: el usuario del worker lo lee, nadie
# lo modifica (ADD desde una URL deja el archivo 600 y el directorio 700, de root: el worker no arrancaría).
ADD --chmod=0444 --checksum=sha256:c5c2d13e59ae883e6af3b45daea64af4833a4951c92d116ec270d9ddbe998063 \
    https://github.com/Megvii-BaseDetection/YOLOX/releases/download/0.1.1rc0/yolox_s.onnx /modelos/yolox_s.onnx
RUN chmod 0555 /modelos

ENV FORENSIC_IA_MODELOS=/modelos \
    HF_HUB_OFFLINE=1 \
    TRANSFORMERS_OFFLINE=1 \
    OPENCV_IO_MAX_IMAGE_PIXELS=100000000

RUN useradd --system --uid 10001 worker
USER worker

EXPOSE 8000
CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000"]
