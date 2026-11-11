# Worker de procesamiento: operaciones deterministas de OpenCV. Las versiones de Python, numpy y
# OpenCV quedan fijadas por la imagen base y pyproject.toml; cambiarlas exige verificar que los
# derivados existentes sigan reproduciendo. Para uso pericial, fijar también el digest (Fase 5).
# Base fijada por digest: la imagen es parte del entorno asentado. Dependabot propone las actualizaciones.
FROM python:3.12-slim-bookworm@sha256:935c3c5b12357a7e10caa9c1e6ca5692344d97cf229832ef584be2cec9fc03df

WORKDIR /srv/worker
COPY pyproject.toml ./
COPY app ./app
RUN pip install --no-cache-dir .

ENV OPENCV_IO_MAX_IMAGE_PIXELS=100000000

RUN useradd --system --uid 10001 worker
USER worker

EXPOSE 8000
CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000"]
