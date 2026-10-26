"""Extracción de un frame: decodificación secuencial hasta el índice, sin seek."""

import json
from pathlib import Path

import cv2
import numpy as np
import pytest

from app import operaciones
from app.operaciones import ParametrosInvalidos
from app.png import codificar_png

FRAMES = 6


@pytest.fixture(scope="module")
def video(tmp_path_factory: pytest.TempPathFactory) -> bytes:
    """Video FFV1 (lossless) con frames de valor constante 0, 40, 80…: cada frame se reconoce exacto."""
    ruta = Path(tmp_path_factory.mktemp("video")) / "muestra.mkv"
    escritor = cv2.VideoWriter(str(ruta), cv2.VideoWriter_fourcc(*"FFV1"), 10, (32, 24))
    assert escritor.isOpened()
    for i in range(FRAMES):
        escritor.write(np.full((24, 32, 3), i * 40, np.uint8))
    escritor.release()
    return ruta.read_bytes()


def extraer(entrada: bytes, **parametros) -> np.ndarray:
    return operaciones.aplicar_a_bytes("opencv.frame/v1", json.dumps(parametros), entrada)


@pytest.mark.parametrize("indice", [0, 3, FRAMES - 1])
def test_extrae_exactamente_el_frame_pedido(video: bytes, indice: int):
    frame = extraer(video, indice=indice)

    assert frame.shape == (24, 32, 3)
    assert frame.dtype == np.uint8
    assert np.all(frame == indice * 40)


def test_extraer_dos_veces_da_los_mismos_bytes(video: bytes):
    assert codificar_png(extraer(video, indice=4)) == codificar_png(extraer(video, indice=4))


def test_un_indice_mas_alla_del_ultimo_frame_es_invalido(video: bytes):
    with pytest.raises(ParametrosInvalidos):
        extraer(video, indice=FRAMES)


def test_un_contenido_que_no_es_video_es_invalido():
    with pytest.raises(ParametrosInvalidos):
        extraer(b"esto no es un video", indice=0)


def test_las_operaciones_de_imagen_rechazan_un_video(video: bytes):
    with pytest.raises(Exception):  # noqa: B017  (ImagenInvalida o ParametrosInvalidos: no es una imagen)
        operaciones.aplicar_a_bytes("opencv.recorte/v1", '{"alto":1,"ancho":1,"x":0,"y":0}', video)
