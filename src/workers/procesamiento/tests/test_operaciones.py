"""Cada operación del catálogo: píxeles correctos, determinismo y validación contra la imagen."""

import json

import cv2
import numpy as np
import pytest

from app import operaciones
from app.operaciones import ParametrosInvalidos
from app.png import codificar_png

rng = np.random.default_rng(seed=11)
BGR = rng.integers(0, 256, (60, 100, 3), dtype=np.uint8)
GRIS_16 = rng.integers(0, 65536, (60, 100), dtype=np.uint16)


def aplicar(operacion: str, pixeles: np.ndarray, **parametros) -> np.ndarray:
    return operaciones.aplicar(operacion, json.dumps(parametros), pixeles)


def esquinas(ancho: int, alto: int) -> dict[str, int]:
    return {
        "x1": 0,
        "y1": 0,
        "x2": ancho - 1,
        "y2": 0,
        "x3": ancho - 1,
        "y3": alto - 1,
        "x4": 0,
        "y4": alto - 1,
    }


# --- perspectiva ---------------------------------------------------------------------------------


def test_perspectiva_identidad_con_vecino_devuelve_la_imagen_exacta():
    salida = aplicar(
        "opencv.perspectiva/v1", BGR, **esquinas(100, 60), ancho=100, alto=60, interpolacion="vecino"
    )

    assert np.array_equal(salida, BGR)


def test_perspectiva_rectifica_un_cuadrilatero_al_tamano_pedido():
    params = {"x1": 10, "y1": 5, "x2": 90, "y2": 2, "x3": 95, "y3": 55, "x4": 4, "y4": 58}

    salida = aplicar("opencv.perspectiva/v1", BGR, **params, ancho=40, alto=30, interpolacion="lineal")

    origen = np.float32([[10, 5], [90, 2], [95, 55], [4, 58]])
    destino = np.float32([[0, 0], [39, 0], [39, 29], [0, 29]])
    esperado = cv2.warpPerspective(
        BGR,
        cv2.getPerspectiveTransform(origen, destino),
        (40, 30),
        flags=cv2.INTER_LINEAR,
        borderMode=cv2.BORDER_CONSTANT,
        borderValue=0,
    )
    assert salida.shape == (30, 40, 3)
    assert np.array_equal(salida, esperado)


def test_perspectiva_es_determinista_en_bytes():
    params = {"x1": 10, "y1": 5, "x2": 90, "y2": 2, "x3": 95, "y3": 55, "x4": 4, "y4": 58}
    a = aplicar("opencv.perspectiva/v1", GRIS_16, **params, ancho=40, alto=30, interpolacion="cubica")
    b = aplicar("opencv.perspectiva/v1", GRIS_16.copy(), **params, ancho=40, alto=30, interpolacion="cubica")

    assert codificar_png(a) == codificar_png(b)
    assert a.dtype == np.uint16


@pytest.mark.parametrize(
    "puntos",
    [
        {"x1": 0, "y1": 0, "x2": 100, "y2": 0, "x3": 99, "y3": 59, "x4": 0, "y4": 59},  # x2 fuera (ancho 100)
        {"x1": 0, "y1": 0, "x2": 50, "y2": 0, "x3": 99, "y3": 0, "x4": 20, "y4": 0},  # colineales
        {"x1": 0, "y1": 0, "x2": 99, "y2": 59, "x3": 99, "y3": 0, "x4": 0, "y4": 59},  # cruzado, no convexo
    ],
)
def test_perspectiva_rechaza_puntos_fuera_de_la_imagen_o_degenerados(puntos):
    with pytest.raises(ParametrosInvalidos):
        aplicar("opencv.perspectiva/v1", BGR, **puntos, ancho=10, alto=10, interpolacion="lineal")
