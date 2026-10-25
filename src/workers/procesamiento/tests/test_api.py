import hashlib
import json

import cv2
import numpy as np
import pytest
from fastapi.testclient import TestClient

from app.main import app
from app.png import codificar_png, decodificar, hash_de_pixeles

cliente = TestClient(app)
rng = np.random.default_rng(seed=7)
IMAGEN = rng.integers(0, 256, (48, 64, 3), dtype=np.uint8)
PNG = codificar_png(IMAGEN)


def aplicar(operacion: str, parametros: str, entrada: bytes = PNG):
    return cliente.post(
        "/v1/operaciones",
        data={"operacion": operacion, "parametros": parametros},
        files={"entrada": ("entrada", entrada, "application/octet-stream")},
    )


def recortar(parametros: str, entrada: bytes = PNG):
    return aplicar("opencv.recorte/v1", parametros, entrada)


def test_el_recorte_devuelve_exactamente_los_pixeles_de_la_region():
    r = recortar('{"alto":20,"ancho":30,"x":5,"y":10}')

    assert r.status_code == 200
    assert r.headers["content-type"] == "image/png"
    assert np.array_equal(decodificar(r.content), IMAGEN[10:30, 5:35])


def test_la_respuesta_reporta_los_hashes_de_entrada_salida_y_pixeles():
    r = recortar('{"alto":20,"ancho":30,"x":5,"y":10}')

    assert r.headers["x-sha256-entrada"] == hashlib.sha256(PNG).hexdigest()
    assert r.headers["x-sha256-salida"] == hashlib.sha256(r.content).hexdigest()
    assert r.headers["x-sha256-pixeles"] == hash_de_pixeles(IMAGEN[10:30, 5:35])


def test_la_respuesta_reporta_el_entorno_clavado():
    entorno = json.loads(recortar('{"alto":1,"ancho":1,"x":0,"y":0}').headers["x-entorno"])

    assert entorno["opencv"] == cv2.__version__
    assert entorno["numpy"] == np.__version__
    assert (entorno["hilos"], entorno["optimizado"], entorno["opencl"]) == (1, False, False)


def test_el_mismo_recorte_da_los_mismos_bytes():
    a = recortar('{"alto":20,"ancho":30,"x":5,"y":10}')
    b = recortar('{"alto":20,"ancho":30,"x":5,"y":10}')

    assert a.content == b.content


def test_el_recorte_de_una_imagen_de_16_bits_conserva_la_profundidad():
    imagen = rng.integers(0, 65536, (20, 20), dtype=np.uint16)

    r = recortar('{"alto":5,"ancho":5,"x":2,"y":3}', codificar_png(imagen))

    assert np.array_equal(decodificar(r.content), imagen[3:8, 2:7])


@pytest.mark.parametrize(
    "parametros",
    [
        '{"alto":20,"ancho":30,"x":40,"y":10}',  # se sale por la derecha (64 de ancho)
        '{"alto":40,"ancho":30,"x":0,"y":10}',  # se sale por abajo (48 de alto)
        '{"alto":0,"ancho":30,"x":0,"y":0}',
        '{"alto":1.5,"ancho":30,"x":0,"y":0}',
        '{"alto":true,"ancho":30,"x":0,"y":0}',
        '{"alto":1,"ancho":1,"x":0}',
        '{"alto":1,"ancho":1,"x":0,"y":0,"extra":1}',
        "[1,1,0,0]",
        "no es json",
    ],
)
def test_parametros_invalidos_o_fuera_de_la_imagen_son_422(parametros: str):
    assert recortar(parametros).status_code == 422


def test_una_operacion_fuera_del_catalogo_es_422():
    assert aplicar("opencv.denoise/v9", '{"h":3}').status_code == 422


def test_un_contenido_que_no_es_imagen_es_422():
    assert recortar('{"alto":1,"ancho":1,"x":0,"y":0}', b"esto no es una imagen").status_code == 422


def test_una_imagen_de_varias_paginas_es_422():
    ok, tiff = cv2.imencodemulti(".tiff", [IMAGEN, IMAGEN])
    assert ok

    assert recortar('{"alto":1,"ancho":1,"x":0,"y":0}', tiff.tobytes()).status_code == 422
