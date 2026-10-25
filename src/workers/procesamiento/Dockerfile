# Worker de procesamiento: operaciones deterministas de OpenCV. Las versiones de Python, numpy y
# OpenCV quedan fijadas por la imagen base y pyproject.toml; cambiarlas exige verificar que los
# derivados existentes sigan reproduciendo. Para uso pericial, fijar también el digest (Fase 5).
FROM python:3.12-slim-bookworm

WORKDIR /srv/worker
COPY pyproject.toml ./
COPY app ./app
RUN pip install --no-cache-dir .

ENV OPENCV_IO_MAX_IMAGE_PIXELS=100000000

RUN useradd --system --uid 10001 worker
USER worker

EXPOSE 8000
CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000"]
