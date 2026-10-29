"""PP-OCRv4 con los pesos reales sobre una foto real (CC0, ver tests/datos/LEEME.md).

test_ocr.py dibuja el texto con OpenCV: mayúsculas y números, negro sobre blanco. Esta foto trae un
cartel real («Crown House», con minúsculas y un espacio) entre semáforos, rejas y marcas pintadas en el
piso: prueba la detección sobre una escena y el diccionario de caracteres del modelo más allá de lo
sintético. El texto del piso («LOOK LEFT») no se lee a la escala del modelo (960 px de lado): el test no
lo pide.
"""

import hashlib
from pathlib import Path

import pytest

from app import modelos, ocr
from app.imagen import a_bgr8, decodificar

FOTO = Path(__file__).parent / "datos" / "chatham-the-brook.jpg"
FOTO_SHA256 = "ed9d5e4960a944b3c49116de6cd4ee8cbb054cf1db16cd7da1bcbb9e30160144"

# El cartel de la pared, medido con onnxruntime en CPU.
CARTEL = {"x": 1440, "y": 49, "ancho": 313, "alto": 108}


@pytest.fixture(scope="module")
def hallazgos(directorio_modelos):
    contenido = FOTO.read_bytes()
    # La foto es la de Commons, sin tocar: si cambió, el test no prueba lo que dice.
    assert hashlib.sha256(contenido).hexdigest() == FOTO_SHA256
    lector = ocr.Lector(modelos.verificar(modelos.MANIFIESTO, directorio_modelos)[ocr.MODELO])
    return lector.leer(a_bgr8(decodificar(contenido)), umbral_milesimas=500)


def _iou(a: dict[str, int], b: dict[str, int]) -> float:
    ancho = min(a["x"] + a["ancho"], b["x"] + b["ancho"]) - max(a["x"], b["x"])
    alto = min(a["y"] + a["alto"], b["y"] + b["alto"]) - max(a["y"], b["y"])
    interseccion = max(ancho, 0) * max(alto, 0)
    return interseccion / (a["ancho"] * a["alto"] + b["ancho"] * b["alto"] - interseccion)


def test_lee_el_cartel_de_una_foto_real(hallazgos):
    cartel = [h for h in hallazgos if h["textoLeido"] == "Crown House"]

    assert len(cartel) == 1, [h["textoLeido"] for h in hallazgos]
    assert cartel[0]["confianza"] >= 0.9


def test_la_region_del_cartel_vuelve_a_los_pixeles_de_la_foto_original(hallazgos):
    # 3296×2367 entra al detector reducida a 960 de lado: un error al deshacer la escala la mueve.
    (cartel,) = [h for h in hallazgos if h["textoLeido"] == "Crown House"]

    assert _iou(CARTEL, cartel["region"]) >= 0.9, cartel["region"]
