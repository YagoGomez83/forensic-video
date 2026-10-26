"""Super-resolución x4 con Real-ESRGAN (ONNX): produce un derivado-imagen «sólo investigación».

Como la implementación de referencia: RGB en [0, 1], salida recortada a [0, 1] y redondeada a 8 bits,
en mosaicos con margen para acotar la memoria. El resultado inventa detalle plausible, no recupera el
real: nunca es prueba.
"""

import json

import cv2
import numpy as np

from app.imagen import ImagenInvalida

OPERACION = "ia.superresolucion/v1"
MODELO = "realesrgan-x4plus"

ESCALA = 4
MOSAICO = 128
MARGEN = 10
MAXIMO_PIXELES_ENTRADA = 1920 * 1080

# Constantes del método: van en el entorno de cada respuesta (y de ahí al asiento).
POSTPROCESO = {"escala": ESCALA, "mosaico": MOSAICO, "margen": MARGEN, "png": "compresion-6"}

# Fijos, como en el worker de procesamiento: el mismo array da siempre los mismos bytes.
_PARAMETROS_PNG = [cv2.IMWRITE_PNG_COMPRESSION, 6, cv2.IMWRITE_PNG_STRATEGY, cv2.IMWRITE_PNG_STRATEGY_DEFAULT]


class ParametrosInvalidos(ValueError):
    """Los parámetros no son los que admite la super-resolución."""


def interpretar_parametros(texto: str) -> str:
    """Sin parámetros: la escala la fija el modelo. Devuelve el JSON canónico."""
    try:
        parametros = json.loads(texto)
    except json.JSONDecodeError as ex:
        raise ParametrosInvalidos("Los parámetros no son JSON.") from ex
    if parametros != {}:
        raise ParametrosInvalidos(
            "La super-resolución no admite parámetros: la escala (x4) la fija el modelo."
        )
    return "{}"


def superresolver(sesion, bgr: np.ndarray) -> np.ndarray:
    alto, ancho = bgr.shape[:2]
    if alto * ancho > MAXIMO_PIXELES_ENTRADA:
        raise ImagenInvalida(
            f"La entrada tiene {alto * ancho} píxeles; "
            f"la super-resolución admite hasta {MAXIMO_PIXELES_ENTRADA}."
        )
    rgb = cv2.cvtColor(bgr, cv2.COLOR_BGR2RGB).astype(np.float32) / 255.0
    entrada = np.ascontiguousarray(rgb.transpose(2, 0, 1)[np.newaxis])
    salida = np.empty((3, alto * ESCALA, ancho * ESCALA), dtype=np.float32)

    for y in range(0, alto, MOSAICO):
        for x in range(0, ancho, MOSAICO):
            # El mosaico con un margen de contexto; de la salida se toma sólo la parte del mosaico.
            y0, x0 = max(y - MARGEN, 0), max(x - MARGEN, 0)
            y1, x1 = min(y + MOSAICO + MARGEN, alto), min(x + MOSAICO + MARGEN, ancho)
            (parcial,) = sesion.run(None, {"entrada": entrada[:, :, y0:y1, x0:x1]})
            th, tw = min(MOSAICO, alto - y) * ESCALA, min(MOSAICO, ancho - x) * ESCALA
            oy, ox = (y - y0) * ESCALA, (x - x0) * ESCALA
            salida[:, y * ESCALA : y * ESCALA + th, x * ESCALA : x * ESCALA + tw] = parcial[
                0, :, oy : oy + th, ox : ox + tw
            ]

    rgb8 = np.round(np.clip(salida, 0, 1).transpose(1, 2, 0) * 255).astype(np.uint8)
    return cv2.cvtColor(rgb8, cv2.COLOR_RGB2BGR)


def codificar_png(bgr: np.ndarray) -> bytes:
    ok, png = cv2.imencode(".png", bgr, _PARAMETROS_PNG)
    if not ok:
        raise ImagenInvalida("OpenCV no pudo codificar el resultado como PNG.")
    return png.tobytes()
