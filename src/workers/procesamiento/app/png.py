"""Codificación PNG determinista y decodificación sin transformaciones ocultas."""

import hashlib

import cv2
import numpy as np

from app import entorno  # noqa: F401  (clava el entorno antes de codificar)

# Fijos: cambiarlos cambia los bytes de todo derivado y exige versionar las operaciones.
_PARAMETROS_PNG = [
    cv2.IMWRITE_PNG_COMPRESSION,
    6,
    cv2.IMWRITE_PNG_STRATEGY,
    cv2.IMWRITE_PNG_STRATEGY_DEFAULT,
]


class ImagenInvalida(ValueError):
    """El contenido no es una imagen que el worker pueda procesar."""


def codificar_png(pixeles: np.ndarray) -> bytes:
    """El mismo array da siempre los mismos bytes: sin chunks de tiempo, texto ni metadatos."""
    ok, png = cv2.imencode(".png", pixeles, _PARAMETROS_PNG)
    if not ok:
        raise ImagenInvalida("OpenCV no pudo codificar el resultado como PNG.")
    return png.tobytes()


_LECTURA = cv2.IMREAD_UNCHANGED | cv2.IMREAD_IGNORE_ORIENTATION


def decodificar(contenido: bytes) -> np.ndarray:
    """Decodifica tal cual: sin aplicar la orientación EXIF ni convertir profundidad o canales.

    Una imagen de varias páginas o animada se rechaza: elegir una página sería una decisión oculta.
    """
    buffer = np.frombuffer(contenido, dtype=np.uint8)
    ok, paginas = cv2.imdecodemulti(buffer, _LECTURA)
    if ok and len(paginas) > 1:
        raise ImagenInvalida(f"La imagen tiene {len(paginas)} páginas o cuadros; se procesa una sola.")
    pixeles = paginas[0] if ok and paginas else cv2.imdecode(buffer, _LECTURA)
    if pixeles is None:
        raise ImagenInvalida("El contenido no es una imagen que OpenCV pueda leer.")
    return pixeles


def hash_de_pixeles(pixeles: np.ndarray) -> str:
    """Hash canónico de los píxeles (dtype, forma y bytes crudos), independiente del encoding."""
    sha = hashlib.sha256()
    sha.update(f"{pixeles.dtype.str}|{'x'.join(map(str, pixeles.shape))}|".encode())
    sha.update(np.ascontiguousarray(pixeles).tobytes())
    return sha.hexdigest()
