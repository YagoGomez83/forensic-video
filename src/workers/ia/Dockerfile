# Worker de IA (sólo investigación). Corre en la workstation con GPU NVIDIA (nvidia-container-toolkit)
# y, sin GPU, cae a CPU con los mismos pesos. Los pesos entran AL CONSTRUIR, verificados contra el
# hash de app/modelos.json; en ejecución nada se descarga: con la red cortada el worker arranca igual.
# Sin sintaxis de BuildKit: la imagen se construye igual con el builder clásico (Testcontainers).
FROM python:3.12-slim-bookworm

WORKDIR /srv/worker
# Primero todas las dependencias con versión exacta (incluidas CUDA y cuDNN, que trae el extra «gpu»
# de onnxruntime-gpu): es la capa pesada y no se rehace al cambiar el código del worker.
COPY restricciones.txt ./
RUN pip install --no-cache-dir --no-compile -r restricciones.txt

# Los pesos, verificados por hash; si alguno no coincide, el build falla. Sólo lectura: el usuario del
# worker los lee, nadie los modifica.
COPY app/modelos.json ./app/modelos.json
COPY scripts/descargar_modelos.py ./scripts/descargar_modelos.py
RUN python scripts/descargar_modelos.py /modelos \
    && chmod 0444 /modelos/* \
    && chmod 0555 /modelos

COPY pyproject.toml ./
COPY app ./app
RUN pip install --no-cache-dir --no-deps .

ENV FORENSIC_IA_MODELOS=/modelos \
    HF_HUB_OFFLINE=1 \
    TRANSFORMERS_OFFLINE=1 \
    OPENCV_IO_MAX_IMAGE_PIXELS=100000000

RUN useradd --system --uid 10001 worker
USER worker

EXPOSE 8000
CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000"]
