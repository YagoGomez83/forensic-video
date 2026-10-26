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


# --- rotación ------------------------------------------------------------------------------------


@pytest.mark.parametrize(
    ("giros", "codigo"),
    [(1, cv2.ROTATE_90_CLOCKWISE), (2, cv2.ROTATE_180), (3, cv2.ROTATE_90_COUNTERCLOCKWISE)],
)
def test_rotacion90_es_exacta_en_sentido_horario(giros: int, codigo: int):
    salida = aplicar("opencv.rotacion90/v1", BGR, giros=giros)

    assert np.array_equal(salida, cv2.rotate(BGR, codigo))
    assert np.array_equal(salida, np.rot90(BGR, k=-giros))


def test_cuatro_cuartos_de_vuelta_devuelven_la_imagen_original():
    salida = BGR
    for _ in range(4):
        salida = aplicar("opencv.rotacion90/v1", salida, giros=1)

    assert np.array_equal(salida, BGR)


def test_rotacion_por_angulo_gira_alrededor_del_centro_y_conserva_el_tamano():
    salida = aplicar("opencv.rotacion/v1", BGR, centesimasDeGrado=1250, interpolacion="lineal")

    matriz = cv2.getRotationMatrix2D((49.5, 29.5), 12.5, 1.0)
    esperado = cv2.warpAffine(
        BGR, matriz, (100, 60), flags=cv2.INTER_LINEAR, borderMode=cv2.BORDER_CONSTANT, borderValue=0
    )
    assert salida.shape == BGR.shape
    assert np.array_equal(salida, esperado)


def test_rotacion_por_angulo_es_determinista_en_bytes():
    a = aplicar("opencv.rotacion/v1", GRIS_16, centesimasDeGrado=-733, interpolacion="cubica")
    b = aplicar("opencv.rotacion/v1", GRIS_16.copy(), centesimasDeGrado=-733, interpolacion="cubica")

    assert codificar_png(a) == codificar_png(b)


@pytest.mark.parametrize(
    "parametros",
    [
        {"giros": 0},
        {"giros": True},
        {"giros": 1, "sentido": "horario"},
    ],
)
def test_rotacion90_rechaza_parametros_invalidos(parametros):
    with pytest.raises(ParametrosInvalidos):
        aplicar("opencv.rotacion90/v1", BGR, **parametros)


# --- denoise -------------------------------------------------------------------------------------

GRIS_8 = rng.integers(0, 256, (60, 100), dtype=np.uint8)
NLMEANS = {"h": 10, "hColor": 12, "plantilla": 7, "busqueda": 21}


def test_nlmeans_en_color_es_fast_nl_means_colored():
    salida = aplicar("opencv.denoise.nlmeans/v1", BGR, **NLMEANS)

    assert np.array_equal(salida, cv2.fastNlMeansDenoisingColored(BGR, None, 10, 12, 7, 21))


def test_nlmeans_en_gris_es_fast_nl_means():
    salida = aplicar("opencv.denoise.nlmeans/v1", GRIS_8, **NLMEANS)

    assert np.array_equal(salida, cv2.fastNlMeansDenoising(GRIS_8, None, 10, 7, 21))


def test_nlmeans_es_determinista_en_bytes():
    a = aplicar("opencv.denoise.nlmeans/v1", BGR, **NLMEANS)
    b = aplicar("opencv.denoise.nlmeans/v1", BGR.copy(), **NLMEANS)

    assert codificar_png(a) == codificar_png(b)


@pytest.mark.parametrize("pixeles", [GRIS_16, rng.integers(0, 256, (20, 20, 4), dtype=np.uint8)])
def test_nlmeans_rechaza_lo_que_no_es_gris_o_bgr_de_8_bits(pixeles):
    with pytest.raises(ParametrosInvalidos):
        aplicar("opencv.denoise.nlmeans/v1", pixeles, **NLMEANS)


@pytest.mark.parametrize("pixeles", [BGR, GRIS_16])
def test_mediana_es_median_blur(pixeles):
    salida = aplicar("opencv.denoise.mediana/v1", pixeles, ventana=5)

    assert np.array_equal(salida, cv2.medianBlur(pixeles, 5))


def test_mediana_de_16_bits_solo_admite_ventanas_de_3_o_5():
    with pytest.raises(ParametrosInvalidos):
        aplicar("opencv.denoise.mediana/v1", GRIS_16, ventana=7)


def test_mediana_rechaza_ventanas_pares():
    with pytest.raises(ParametrosInvalidos):
        aplicar("opencv.denoise.mediana/v1", BGR, ventana=4)


# --- realce --------------------------------------------------------------------------------------


@pytest.mark.parametrize("pixeles", [BGR, GRIS_16])
def test_unsharp_es_la_imagen_mas_su_detalle_con_saturacion(pixeles):
    salida = aplicar("opencv.realce.unsharp/v1", pixeles, sigmaCentesimas=150, cantidadCentesimas=80)

    desenfocada = cv2.GaussianBlur(pixeles, (0, 0), 1.5)
    assert salida.dtype == pixeles.dtype
    assert np.array_equal(salida, cv2.addWeighted(pixeles, 1.8, desenfocada, -0.8, 0))


def test_unsharp_es_determinista_en_bytes():
    a = aplicar("opencv.realce.unsharp/v1", BGR, sigmaCentesimas=237, cantidadCentesimas=133)
    b = aplicar("opencv.realce.unsharp/v1", BGR.copy(), sigmaCentesimas=237, cantidadCentesimas=133)

    assert codificar_png(a) == codificar_png(b)


@pytest.mark.parametrize("pixeles", [GRIS_8, GRIS_16])
def test_clahe_en_gris_es_create_clahe_apply(pixeles):
    salida = aplicar("opencv.realce.clahe/v1", pixeles, limiteDecimas=20, cuadricula=8)

    assert np.array_equal(salida, cv2.createCLAHE(clipLimit=2.0, tileGridSize=(8, 8)).apply(pixeles))


def test_clahe_en_color_se_aplica_sobre_la_luminancia_de_lab():
    salida = aplicar("opencv.realce.clahe/v1", BGR, limiteDecimas=20, cuadricula=8)

    lab = cv2.cvtColor(BGR, cv2.COLOR_BGR2LAB)
    lab[:, :, 0] = cv2.createCLAHE(clipLimit=2.0, tileGridSize=(8, 8)).apply(lab[:, :, 0])
    assert np.array_equal(salida, cv2.cvtColor(lab, cv2.COLOR_LAB2BGR))


def test_clahe_rechaza_color_de_16_bits_o_con_alfa():
    with pytest.raises(ParametrosInvalidos):
        aplicar(
            "opencv.realce.clahe/v1",
            rng.integers(0, 65536, (20, 20, 3), dtype=np.uint16),
            limiteDecimas=20,
            cuadricula=8,
        )
    with pytest.raises(ParametrosInvalidos):
        aplicar(
            "opencv.realce.clahe/v1",
            rng.integers(0, 256, (20, 20, 4), dtype=np.uint8),
            limiteDecimas=20,
            cuadricula=8,
        )
