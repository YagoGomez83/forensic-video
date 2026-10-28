# Worker de ingesta. La versión de ffmpeg queda fijada por la imagen base; para uso pericial
# conviene fijar también el digest de la imagen (Fase 5: imágenes firmadas).
# Base fijada por digest: la imagen es parte del entorno asentado. Dependabot propone las actualizaciones.
FROM python:3.12-slim-bookworm@sha256:e697c81897302a4c4333bade000d98038fbb21eb064f8ee1fd42d02ebcd9d337

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
