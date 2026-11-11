# Worker de ingesta. La versión de ffmpeg queda fijada por la imagen base; para uso pericial
# conviene fijar también el digest de la imagen (Fase 5: imágenes firmadas).
# Base fijada por digest: la imagen es parte del entorno asentado. Dependabot propone las actualizaciones.
FROM python:3.12-slim-bookworm@sha256:935c3c5b12357a7e10caa9c1e6ca5692344d97cf229832ef584be2cec9fc03df

RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /srv/worker
COPY pyproject.toml ./
COPY app ./app
RUN pip install --no-cache-dir .

RUN useradd --system --uid 10001 worker
USER worker

EXPOSE 8000
CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000"]
