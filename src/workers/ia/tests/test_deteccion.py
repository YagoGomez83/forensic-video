import math

import cv2
import numpy as np
import pytest

from app import deteccion
from app.imagen import ImagenInvalida, a_bgr8, decodificar


def _salida_vacia() -> np.ndarray:
    # Logits que, tras el decodificado, no superan ningún umbral: objetividad 0.
    return np.zeros((1, deteccion.ANCLAS, 85), dtype=np.float32)


def _ancla(salida, gx, gy, dx, dy, log_w, log_h, objetividad, clase, puntaje, paso=8):
    """Escribe una predicción cruda en la celda (gx, gy) de la grilla del paso dado."""
    lado = deteccion.LADO // paso
    desplazamiento = {
        8: 0,
        16: (deteccion.LADO // 8) ** 2,
        32: (deteccion.LADO // 8) ** 2 + (deteccion.LADO // 16) ** 2,
    }
    k = desplazamiento[paso] + gy * lado + gx
    salida[0, k, 0:4] = (dx, dy, log_w, log_h)
    salida[0, k, 4] = objetividad
    salida[0, k, 5 + clase] = puntaje


# --- Preprocesado (letterbox de YOLOX) ---


def test_preprocesar_escala_sin_deformar_y_rellena_con_114():
    bgr = np.zeros((160, 320, 3), dtype=np.uint8)
    bgr[:, :, 0] = 200  # azul: el modelo recibe BGR, sin reordenar canales

    tensor, razon = deteccion.preprocesar(bgr)

    assert razon == 2.0
    assert tensor.shape == (1, 3, deteccion.LADO, deteccion.LADO)
    assert tensor.dtype == np.float32
    assert (tensor[0, 0, :320, :] == 200).all()
    assert (tensor[0, 1:, :320, :] == 0).all()
    assert (tensor[0, :, 320:, :] == 114).all()


def test_preprocesar_no_normaliza_los_valores():
    bgr = np.full((640, 640, 3), 255, dtype=np.uint8)

    tensor, razon = deteccion.preprocesar(bgr)

    assert razon == 1.0
    assert tensor.max() == 255.0


# --- Postprocesado ---


def test_postprocesar_decodifica_la_grilla_y_vuelve_a_pixeles_de_la_imagen():
    salida = _salida_vacia()
    # Celda (10, 5) del paso 8: centro (10.55 * 8, 5.55 * 8) = (84.4, 44.4); 32 x 16 en el letterbox.
    # Bordes no enteros a propósito: en float32 un borde «entero» cae a un pelo y ceil lo corre.
    _ancla(salida, 10, 5, 0.55, 0.55, math.log(4), math.log(2), 0.9, 2, 0.9)

    hallazgos = deteccion.postprocesar(salida, razon=2.0, ancho=320, alto=160, umbral_milesimas=500)

    assert len(hallazgos) == 1
    h = hallazgos[0]
    assert h["clase"] == "car"
    assert h["confianza"] == pytest.approx(0.81, abs=1e-6)
    # Caja (68.4, 36.4)-(100.4, 52.4) en el letterbox; / 2 = (34.2, 18.2)-(50.2, 26.2) en la imagen.
    assert h["region"] == {"x": 34, "y": 18, "ancho": 17, "alto": 9}
    crudo = h["crudo"]
    assert crudo["claseIndice"] == 2
    assert crudo["caja"] == pytest.approx([34.2, 18.2, 50.2, 26.2])
    assert crudo["objetividad"] == pytest.approx(0.9)
    assert crudo["puntajeClase"] == pytest.approx(0.9)
    assert crudo["puntaje"] == h["confianza"]


def test_la_region_es_la_envolvente_entera_de_la_caja():
    salida = _salida_vacia()
    _ancla(salida, 10, 5, 0.3, 0.3, math.log(3.3), math.log(3.3), 0.9, 0, 0.9)

    (h,) = deteccion.postprocesar(salida, razon=1.0, ancho=640, alto=640, umbral_milesimas=500)

    x1, y1, x2, y2 = h["crudo"]["caja"]
    r = h["region"]
    assert (r["x"], r["y"]) == (math.floor(x1), math.floor(y1))
    assert (r["x"] + r["ancho"], r["y"] + r["alto"]) == (math.ceil(x2), math.ceil(y2))


def test_postprocesar_respeta_el_umbral():
    salida = _salida_vacia()
    _ancla(salida, 10, 5, 0.5, 0.5, math.log(4), math.log(2), 0.9, 2, 0.9)  # 0.81

    assert deteccion.postprocesar(salida, razon=1.0, ancho=640, alto=640, umbral_milesimas=810) == []
    assert len(deteccion.postprocesar(salida, razon=1.0, ancho=640, alto=640, umbral_milesimas=809)) == 1


def test_nms_por_clase_suprime_solapadas_de_la_misma_clase_y_no_las_de_otra():
    salida = _salida_vacia()
    _ancla(salida, 10, 5, 0.5, 0.5, math.log(8), math.log(8), 0.95, 2, 0.95)
    # Misma clase, casi la misma caja, menor puntaje: se suprime.
    _ancla(salida, 11, 5, -0.4, 0.5, math.log(8), math.log(8), 0.9, 2, 0.9)
    # Otra clase en el mismo lugar: se conserva.
    _ancla(salida, 10, 6, 0.5, -0.5, math.log(8), math.log(8), 0.9, 0, 0.9)

    hallazgos = deteccion.postprocesar(salida, razon=1.0, ancho=640, alto=640, umbral_milesimas=500)

    assert sorted(h["clase"] for h in hallazgos) == ["car", "person"]
    auto = next(h for h in hallazgos if h["clase"] == "car")
    assert auto["confianza"] == pytest.approx(0.95 * 0.95, abs=1e-6)


def test_los_hallazgos_salen_ordenados_por_confianza_descendente():
    salida = _salida_vacia()
    _ancla(salida, 2, 2, 0.5, 0.5, 0.0, 0.0, 0.6, 0, 0.9)
    _ancla(salida, 40, 40, 0.5, 0.5, 0.0, 0.0, 0.9, 0, 0.9)

    hallazgos = deteccion.postprocesar(salida, razon=1.0, ancho=640, alto=640, umbral_milesimas=100)

    assert [h["confianza"] for h in hallazgos] == sorted((h["confianza"] for h in hallazgos), reverse=True)


def test_una_caja_que_se_sale_de_la_imagen_se_recorta_al_borde():
    salida = _salida_vacia()
    # Centro (4.4, 4.4), 32 x 32: se sale por arriba y por la izquierda.
    _ancla(salida, 0, 0, 0.55, 0.55, math.log(4), math.log(4), 0.9, 0, 0.9)

    (h,) = deteccion.postprocesar(salida, razon=1.0, ancho=640, alto=640, umbral_milesimas=500)

    assert h["region"] == {"x": 0, "y": 0, "ancho": 21, "alto": 21}
    # El crudo conserva la caja sin recortar: es lo que dijo el modelo.
    assert h["crudo"]["caja"] == pytest.approx([-11.6, -11.6, 20.4, 20.4])


def test_una_caja_en_el_relleno_fuera_de_la_imagen_se_descarta():
    salida = _salida_vacia()
    # Imagen de 640 x 320 (razón 1): todo lo que esté debajo de y = 320 es relleno.
    _ancla(salida, 40, 60, 0.5, 0.5, 0.0, 0.0, 0.9, 0, 0.9)

    assert deteccion.postprocesar(salida, razon=1.0, ancho=640, alto=320, umbral_milesimas=500) == []


# --- Parámetros ---


def test_parametros_validos_se_canonicalizan():
    umbral, canonicos = deteccion.interpretar_parametros('{ "umbralConfianzaMilesimas" : 500 }')

    assert umbral == 500
    assert canonicos == '{"umbralConfianzaMilesimas":500}'


@pytest.mark.parametrize(
    "parametros",
    [
        "no es json",
        "[]",
        "{}",
        '{"umbralConfianzaMilesimas":0}',
        '{"umbralConfianzaMilesimas":1000}',
        '{"umbralConfianzaMilesimas":0.5}',
        '{"umbralConfianzaMilesimas":true}',
        '{"umbralConfianzaMilesimas":"500"}',
        '{"umbralConfianzaMilesimas":500,"otro":1}',
    ],
)
def test_parametros_invalidos_se_rechazan(parametros):
    with pytest.raises(deteccion.ParametrosInvalidos):
        deteccion.interpretar_parametros(parametros)


# --- Imagen de entrada ---


def _png(pixeles: np.ndarray) -> bytes:
    return cv2.imencode(".png", pixeles)[1].tobytes()


def test_gris_y_alfa_pasan_a_bgr_de_8_bits():
    gris = decodificar(_png(np.full((4, 4), 77, dtype=np.uint8)))
    bgra = decodificar(_png(np.full((4, 4, 4), 77, dtype=np.uint8)))

    assert a_bgr8(gris).shape == (4, 4, 3)
    assert a_bgr8(bgra).shape == (4, 4, 3)
    assert (a_bgr8(gris) == 77).all()


def test_16_bits_se_escala_a_8():
    pixeles = decodificar(_png(np.full((4, 4, 3), 65535, dtype=np.uint16)))

    bgr = a_bgr8(pixeles)

    assert bgr.dtype == np.uint8
    assert (bgr == 255).all()


def test_contenido_que_no_es_imagen_se_rechaza():
    with pytest.raises(ImagenInvalida):
        decodificar(b"no es una imagen")


def test_una_imagen_de_varias_paginas_se_rechaza(tmp_path):
    ruta = tmp_path / "dos.tiff"
    cv2.imwritemulti(str(ruta), [np.zeros((4, 4), np.uint8), np.ones((4, 4), np.uint8)])

    with pytest.raises(ImagenInvalida, match="páginas"):
        decodificar(ruta.read_bytes())
