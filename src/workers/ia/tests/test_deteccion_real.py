"""YOLOX-s con los pesos reales sobre una foto real (CC0, ver tests/datos/LEEME.md).

test_deteccion.py prueba el código contra lo que supone de YOLOX (entrada BGR sin normalizar, 80
clases, la grilla de anclas) armando la salida del modelo a mano, y test_api.py usa imágenes sintéticas
donde no hay nada que encontrar. Éste prueba esas suposiciones contra el modelo: si una cambia junto con
su test sintético (normalizar la entrada a [0, 1], por ejemplo), acá deja de encontrar personas y autos.
"""

import hashlib
from collections import Counter
from pathlib import Path

import pytest

from app import deteccion, modelos
from app.imagen import a_bgr8, decodificar

FOTO = Path(__file__).parent / "datos" / "hyde-st-sf.jpg"
FOTO_SHA256 = "24f4c03c6046fe388499c8768e9a3acaf75f58f7757786fd7cbfd70483443089"

# Los dos autos más seguros, medidos con onnxruntime en CPU: un SUV y un sedán blanco a la derecha.
AUTOS_DE_REFERENCIA = (
    {"x": 1714, "y": 900, "ancho": 270, "alto": 204},
    {"x": 1244, "y": 873, "ancho": 368, "alto": 286},
)


@pytest.fixture(scope="module")
def hallazgos(directorio_modelos):
    contenido = FOTO.read_bytes()
    # La foto es la de Commons, sin tocar: si cambió, el test no prueba lo que dice.
    assert hashlib.sha256(contenido).hexdigest() == FOTO_SHA256
    sesion = modelos.abrir_sesion(modelos.verificar(modelos.MANIFIESTO, directorio_modelos)["yolox-s"])
    return deteccion.detectar(sesion, a_bgr8(decodificar(contenido)), 500)


def _iou(a: dict[str, int], b: dict[str, int]) -> float:
    ancho = min(a["x"] + a["ancho"], b["x"] + b["ancho"]) - max(a["x"], b["x"])
    alto = min(a["y"] + a["alto"], b["y"] + b["alto"]) - max(a["y"], b["y"])
    interseccion = max(ancho, 0) * max(alto, 0)
    return interseccion / (a["ancho"] * a["alto"] + b["ancho"] * b["alto"] - interseccion)


def test_encuentra_las_personas_y_los_autos_de_una_calle_real(hallazgos):
    # En la foto hay unas diez personas y nueve autos; el margen cubre las diferencias de decodificación
    # JPEG entre plataformas, no un modelo que deja de ver.
    clases = Counter(h["clase"] for h in hallazgos)

    assert clases["person"] >= 8
    assert clases["car"] >= 7
    assert all(h["confianza"] >= 0.5 for h in hallazgos)


def test_las_regiones_vuelven_a_los_pixeles_de_la_foto_original(hallazgos):
    # 2048×1366 entra al modelo a escala 1/3,2: un error al deshacerla mueve o encoge las regiones.
    autos = sorted((h for h in hallazgos if h["clase"] == "car"), key=lambda h: -h["confianza"])

    for referencia in AUTOS_DE_REFERENCIA:
        mejor = max(_iou(referencia, h["region"]) for h in autos[:4])
        assert mejor >= 0.9, (referencia, [h["region"] for h in autos[:4]])
