"""Lectura de la imagen de entrada, con las mismas reglas que el worker de procesamiento."""

import cv2
import numpy as np

_LECTURA = cv2.IMREAD_UNCHANGED | cv2.IMREAD_IGNORE_ORIENTATION


class ImagenInvalida(ValueError):
    """El contenido no es una imagen que el worker pueda analizar."""


def decodificar(contenido: bytes) -> np.ndarray:
    """Decodifica tal cual: sin aplicar la orientación EXIF. Varias páginas o cuadros se rechazan."""
    buffer = np.frombuffer(contenido, dtype=np.uint8)
    ok, paginas = cv2.imdecodemulti(buffer, _LECTURA)
    if ok and len(paginas) > 1:
        raise ImagenInvalida(f"La imagen tiene {len(paginas)} páginas o cuadros; se analiza una sola.")
    pixeles = paginas[0] if ok and paginas else cv2.imdecode(buffer, _LECTURA)
    if pixeles is None:
        raise ImagenInvalida("El contenido no es una imagen que OpenCV pueda leer.")
    return pixeles


def a_bgr8(pixeles: np.ndarray) -> np.ndarray:
    """Lo que espera el modelo: BGR de 8 bits. Gris se replica, el alfa se descarta, 16 bits se escala."""
    if pixeles.dtype == np.uint16:
        pixeles = np.round(pixeles / 257.0).astype(np.uint8)
    elif pixeles.dtype != np.uint8:
        raise ImagenInvalida(f"Profundidad no soportada: {pixeles.dtype}.")
    if pixeles.ndim == 2:
        return cv2.cvtColor(pixeles, cv2.COLOR_GRAY2BGR)
    if pixeles.ndim == 3 and pixeles.shape[2] == 3:
        return pixeles
    if pixeles.ndim == 3 and pixeles.shape[2] == 4:
        return cv2.cvtColor(pixeles, cv2.COLOR_BGRA2BGR)
    raise ImagenInvalida(f"Cantidad de canales no soportada: {pixeles.shape}.")
