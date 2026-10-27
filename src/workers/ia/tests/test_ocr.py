import cv2
import numpy as np
import pytest

from app import modelos, ocr


@pytest.fixture(scope="module")
def lector(directorio_modelos):
    return ocr.Lector(modelos.verificar(modelos.MANIFIESTO, directorio_modelos)[ocr.MODELO])


def _texto(lineas: list[tuple[str, int]], ancho=480, alto=200) -> np.ndarray:
    """Texto negro sobre blanco, dibujado con OpenCV: sintético, sin licencias de por medio."""
    img = np.full((alto, ancho, 3), 255, np.uint8)
    for texto, y in lineas:
        cv2.putText(img, texto, (40, y), cv2.FONT_HERSHEY_SIMPLEX, 1.6, (0, 0, 0), 4)
    return img


def test_lee_una_linea_de_texto(lector):
    hallazgos = lector.leer(_texto([("ABC123", 110)]), umbral_milesimas=500)

    (h,) = hallazgos
    assert h["textoLeido"] == "ABC123"
    assert h["confianza"] > 0.9
    r = h["region"]
    # La región envuelve el texto dibujado (desde x = 40, línea base en y = 110).
    assert r["x"] <= 45 and r["y"] <= 80 and r["x"] + r["ancho"] >= 200 and r["y"] + r["alto"] >= 105
    crudo = h["crudo"]
    assert len(crudo["poligono"]) == 4
    assert [c["caracter"] for c in crudo["caracteres"]] == list("ABC123")
    assert all(0 < c["probabilidad"] <= 1 for c in crudo["caracteres"])
    assert crudo["puntajeReconocimiento"] == h["confianza"]
    assert crudo["puntajeDeteccion"] >= ocr.UMBRAL_CAJA


def test_lee_varias_lineas_de_arriba_hacia_abajo(lector):
    hallazgos = lector.leer(_texto([("XYZ789", 150), ("ABC123", 60)]), umbral_milesimas=500)

    assert [h["textoLeido"] for h in hallazgos] == ["ABC123", "XYZ789"]


def test_una_imagen_sin_texto_no_tiene_hallazgos(lector):
    assert lector.leer(np.full((100, 100, 3), 255, np.uint8), umbral_milesimas=500) == []


def test_el_umbral_filtra_por_la_confianza_del_reconocimiento(lector):
    imagen = _texto([("ABC123", 110)])
    (h,) = lector.leer(imagen, umbral_milesimas=1)
    justo_encima = int(h["confianza"] * 1000) + 1

    assert lector.leer(imagen, umbral_milesimas=justo_encima) == []


def test_en_cpu_da_lo_mismo_dos_veces(lector):
    imagen = _texto([("ABC123", 110)])

    assert lector.leer(imagen, umbral_milesimas=1) == lector.leer(imagen, umbral_milesimas=1)


def test_decodificar_ctc_quita_repetidos_y_blancos_y_promedia_las_probabilidades():
    diccionario = ["A", "B"]
    # Índices: 0 = blanco, 1 = A, 2 = B. Secuencia: A A _ A B B → "AAB".
    indices = [1, 1, 0, 1, 2, 2]
    probabilidades = np.zeros((6, 4), np.float32)
    for t, (i, p) in enumerate(zip(indices, [0.9, 0.8, 0.99, 0.7, 0.6, 0.5], strict=True)):
        probabilidades[t, i] = p

    texto, confianza, caracteres = ocr.decodificar_ctc(probabilidades, diccionario)

    assert texto == "AAB"
    assert [c["caracter"] for c in caracteres] == ["A", "A", "B"]
    assert [c["probabilidad"] for c in caracteres] == pytest.approx([0.9, 0.7, 0.6])
    assert confianza == pytest.approx((0.9 + 0.7 + 0.6) / 3)


def test_el_ultimo_indice_es_el_espacio():
    probabilidades = np.zeros((3, 4), np.float32)
    probabilidades[0, 1] = probabilidades[1, 3] = probabilidades[2, 2] = 1.0

    texto, _, _ = ocr.decodificar_ctc(probabilidades, ["A", "B"])

    assert texto == "A B"
