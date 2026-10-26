"""Catálogo de operaciones deterministas. Espejo del CatalogoDeOperaciones del dominio.

La entrada no es confiable: los parámetros se validan acá otra vez (tipos exactos, sin campos de más)
y contra las dimensiones reales de la imagen, que el servidor no conoce.
"""

import json
import math
import tempfile
from collections.abc import Callable
from pathlib import Path
from typing import Any

import cv2
import numpy as np

from app.png import decodificar

LADO_MAXIMO = 65_535
PIXELES_MAXIMOS = 100_000_000

INTERPOLACIONES = {"vecino": cv2.INTER_NEAREST, "lineal": cv2.INTER_LINEAR, "cubica": cv2.INTER_CUBIC}


class ParametrosInvalidos(ValueError):
    """Los parámetros no corresponden a la operación o a la imagen."""


class Parametros:
    def __init__(self, operacion: str, texto: str):
        try:
            valores = json.loads(texto)
        except json.JSONDecodeError as ex:
            raise ParametrosInvalidos(f"Los parámetros de '{operacion}' no son JSON válido.") from ex
        if not isinstance(valores, dict):
            raise ParametrosInvalidos(f"Los parámetros de '{operacion}' deben ser un objeto JSON.")
        self._operacion = operacion
        self._valores: dict[str, Any] = valores
        self._leidos: set[str] = set()

    def entero(self, nombre: str, minimo: int, maximo: int) -> int:
        self._leidos.add(nombre)
        if nombre not in self._valores:
            raise ParametrosInvalidos(f"'{self._operacion}' requiere el parámetro '{nombre}'.")
        valor = self._valores[nombre]
        # bool es subclase de int en Python: se excluye a mano.
        if not isinstance(valor, int) or isinstance(valor, bool):
            raise ParametrosInvalidos(f"El parámetro '{nombre}' de '{self._operacion}' debe ser un entero.")
        if not minimo <= valor <= maximo:
            raise ParametrosInvalidos(f"El parámetro '{nombre}' debe estar entre {minimo} y {maximo}.")
        return valor

    def opcion(self, nombre: str, opciones: tuple[str, ...]) -> str:
        self._leidos.add(nombre)
        if nombre not in self._valores:
            raise ParametrosInvalidos(f"'{self._operacion}' requiere el parámetro '{nombre}'.")
        valor = self._valores[nombre]
        if not isinstance(valor, str) or valor not in opciones:
            raise ParametrosInvalidos(f"El parámetro '{nombre}' debe ser uno de: {', '.join(opciones)}.")
        return valor

    def exigir_sin_sobrantes(self) -> None:
        sobrantes = sorted(set(self._valores) - self._leidos)
        if sobrantes:
            raise ParametrosInvalidos(
                f"'{self._operacion}' no admite los parámetros: {', '.join(sobrantes)}."
            )


def recorte(pixeles: np.ndarray, p: Parametros) -> np.ndarray:
    x, y = p.entero("x", 0, LADO_MAXIMO), p.entero("y", 0, LADO_MAXIMO)
    ancho, alto = p.entero("ancho", 1, LADO_MAXIMO), p.entero("alto", 1, LADO_MAXIMO)
    p.exigir_sin_sobrantes()
    alto_imagen, ancho_imagen = pixeles.shape[:2]
    if x + ancho > ancho_imagen or y + alto > alto_imagen:
        raise ParametrosInvalidos(
            f"La región ({x},{y}) {ancho}x{alto} se sale de la imagen de {ancho_imagen}x{alto_imagen}."
        )
    return np.ascontiguousarray(pixeles[y : y + alto, x : x + ancho])


def _interpolacion(p: Parametros) -> int:
    return INTERPOLACIONES[p.opcion("interpolacion", tuple(INTERPOLACIONES))]


def perspectiva(pixeles: np.ndarray, p: Parametros) -> np.ndarray:
    alto_imagen, ancho_imagen = pixeles.shape[:2]
    puntos = []
    for i in range(1, 5):
        x, y = p.entero(f"x{i}", 0, LADO_MAXIMO), p.entero(f"y{i}", 0, LADO_MAXIMO)
        if x >= ancho_imagen or y >= alto_imagen:
            raise ParametrosInvalidos(
                f"El punto {i} ({x},{y}) cae fuera de la imagen de {ancho_imagen}x{alto_imagen}."
            )
        puntos.append((x, y))
    ancho, alto = p.entero("ancho", 1, LADO_MAXIMO), p.entero("alto", 1, LADO_MAXIMO)
    if ancho * alto > PIXELES_MAXIMOS:
        raise ParametrosInvalidos(f"La salida no puede superar {PIXELES_MAXIMOS} píxeles.")
    interpolacion = _interpolacion(p)
    p.exigir_sin_sobrantes()

    origen = np.float32(puntos)
    if not cv2.isContourConvex(origen) or cv2.contourArea(origen) <= 0:
        raise ParametrosInvalidos("Los cuatro puntos deben formar un cuadrilátero convexo, en orden.")
    destino = np.float32([[0, 0], [ancho - 1, 0], [ancho - 1, alto - 1], [0, alto - 1]])
    matriz = cv2.getPerspectiveTransform(origen, destino)
    return cv2.warpPerspective(
        pixeles, matriz, (ancho, alto), flags=interpolacion, borderMode=cv2.BORDER_CONSTANT, borderValue=0
    )


_GIROS = {1: cv2.ROTATE_90_CLOCKWISE, 2: cv2.ROTATE_180, 3: cv2.ROTATE_90_COUNTERCLOCKWISE}


def rotacion90(pixeles: np.ndarray, p: Parametros) -> np.ndarray:
    giros = p.entero("giros", 1, 3)
    p.exigir_sin_sobrantes()
    return cv2.rotate(pixeles, _GIROS[giros])


def rotacion(pixeles: np.ndarray, p: Parametros) -> np.ndarray:
    centesimas = p.entero("centesimasDeGrado", -36_000, 36_000)
    if centesimas == 0:
        raise ParametrosInvalidos("El ángulo de rotación no puede ser cero.")
    interpolacion = _interpolacion(p)
    p.exigir_sin_sobrantes()
    alto, ancho = pixeles.shape[:2]
    # Centro geométrico exacto en coordenadas de píxel; positivo = antihorario (convención de OpenCV).
    matriz = cv2.getRotationMatrix2D(((ancho - 1) / 2, (alto - 1) / 2), centesimas / 100, 1.0)
    return cv2.warpAffine(
        pixeles, matriz, (ancho, alto), flags=interpolacion, borderMode=cv2.BORDER_CONSTANT, borderValue=0
    )


def _impar(p: Parametros, nombre: str, minimo: int, maximo: int) -> int:
    valor = p.entero(nombre, minimo, maximo)
    if valor % 2 == 0:
        raise ParametrosInvalidos(f"La ventana '{nombre}' debe ser impar.")
    return valor


def _es_gris_o_bgr(pixeles: np.ndarray) -> bool:
    return pixeles.ndim == 2 or (pixeles.ndim == 3 and pixeles.shape[2] == 3)


def denoise_nlmeans(pixeles: np.ndarray, p: Parametros) -> np.ndarray:
    h, h_color = p.entero("h", 1, 100), p.entero("hColor", 0, 100)
    plantilla, busqueda = _impar(p, "plantilla", 3, 21), _impar(p, "busqueda", 3, 51)
    p.exigir_sin_sobrantes()
    if busqueda < plantilla:
        raise ParametrosInvalidos("La ventana de búsqueda no puede ser menor que la plantilla.")
    if pixeles.dtype != np.uint8 or not _es_gris_o_bgr(pixeles):
        raise ParametrosInvalidos("NL-means admite imágenes en gris o BGR de 8 bits.")
    if pixeles.ndim == 2:
        return cv2.fastNlMeansDenoising(pixeles, None, h, plantilla, busqueda)
    return cv2.fastNlMeansDenoisingColored(pixeles, None, h, h_color, plantilla, busqueda)


def denoise_mediana(pixeles: np.ndarray, p: Parametros) -> np.ndarray:
    ventana = _impar(p, "ventana", 3, 15)
    p.exigir_sin_sobrantes()
    if pixeles.dtype != np.uint8 and ventana > 5:
        raise ParametrosInvalidos("En imágenes de más de 8 bits la mediana sólo admite ventanas de 3 o 5.")
    return cv2.medianBlur(pixeles, ventana)


def realce_unsharp(pixeles: np.ndarray, p: Parametros) -> np.ndarray:
    sigma = p.entero("sigmaCentesimas", 10, 5000) / 100
    cantidad = p.entero("cantidadCentesimas", 1, 1000) / 100
    p.exigir_sin_sobrantes()
    desenfocada = cv2.GaussianBlur(pixeles, (0, 0), sigma)
    # addWeighted satura al rango del tipo: no hay desbordes ni conversión de profundidad.
    return cv2.addWeighted(pixeles, 1 + cantidad, desenfocada, -cantidad, 0)


def realce_clahe(pixeles: np.ndarray, p: Parametros) -> np.ndarray:
    limite = p.entero("limiteDecimas", 1, 400) / 10
    cuadricula = p.entero("cuadricula", 1, 64)
    p.exigir_sin_sobrantes()
    clahe = cv2.createCLAHE(clipLimit=limite, tileGridSize=(cuadricula, cuadricula))
    if pixeles.ndim == 2:
        return clahe.apply(pixeles)
    if pixeles.dtype != np.uint8 or pixeles.shape[2] != 3:
        raise ParametrosInvalidos("CLAHE admite gris de 8 o 16 bits y BGR de 8 bits.")
    # En 8 bits la conversión a Lab de OpenCV es de punto fijo: no introduce floats.
    lab = cv2.cvtColor(pixeles, cv2.COLOR_BGR2LAB)
    lab[:, :, 0] = clahe.apply(lab[:, :, 0])
    return cv2.cvtColor(lab, cv2.COLOR_LAB2BGR)


def _maximo_del_tipo(pixeles: np.ndarray) -> int:
    if pixeles.dtype == np.uint8:
        return 255
    if pixeles.dtype == np.uint16:
        return 65_535
    raise ParametrosInvalidos(f"Profundidad no admitida: {pixeles.dtype}.")


def brillo_contraste(pixeles: np.ndarray, p: Parametros) -> np.ndarray:
    contraste = p.entero("contrasteCentesimas", 1, 1000) / 100
    maximo = _maximo_del_tipo(pixeles)
    brillo = p.entero("brillo", -maximo, maximo)
    p.exigir_sin_sobrantes()
    # addWeighted con peso 0 en la segunda: contraste·x + brillo, con redondeo y saturación de OpenCV.
    return cv2.addWeighted(pixeles, contraste, pixeles, 0, brillo)


def gamma(pixeles: np.ndarray, p: Parametros) -> np.ndarray:
    exponente = 1000 / p.entero("gammaMilesimas", 100, 10_000)
    p.exigir_sin_sobrantes()
    maximo = _maximo_del_tipo(pixeles)
    # LUT con math.pow (libm) y redondeo explícito hacia arriba en .5: determinista en un mismo entorno.
    tabla = np.array(
        [math.floor(maximo * math.pow(i / maximo, exponente) + 0.5) for i in range(maximo + 1)],
        dtype=pixeles.dtype,
    )
    return cv2.LUT(pixeles, tabla) if pixeles.dtype == np.uint8 else tabla[pixeles]


def ecualizacion(pixeles: np.ndarray, p: Parametros) -> np.ndarray:
    p.exigir_sin_sobrantes()
    if pixeles.dtype != np.uint8 or not _es_gris_o_bgr(pixeles):
        raise ParametrosInvalidos("La ecualización admite gris o BGR de 8 bits.")
    if pixeles.ndim == 2:
        return cv2.equalizeHist(pixeles)
    ycrcb = cv2.cvtColor(pixeles, cv2.COLOR_BGR2YCrCb)
    ycrcb[:, :, 0] = cv2.equalizeHist(ycrcb[:, :, 0])
    return cv2.cvtColor(ycrcb, cv2.COLOR_YCrCb2BGR)


CATALOGO: dict[str, Callable[[np.ndarray, Parametros], np.ndarray]] = {
    "opencv.recorte/v1": recorte,
    "opencv.perspectiva/v1": perspectiva,
    "opencv.rotacion90/v1": rotacion90,
    "opencv.rotacion/v1": rotacion,
    "opencv.denoise.nlmeans/v1": denoise_nlmeans,
    "opencv.denoise.mediana/v1": denoise_mediana,
    "opencv.realce.unsharp/v1": realce_unsharp,
    "opencv.realce.clahe/v1": realce_clahe,
    "opencv.tonal.brillo-contraste/v1": brillo_contraste,
    "opencv.tonal.gamma/v1": gamma,
    "opencv.tonal.ecualizacion/v1": ecualizacion,
}


def extraccion_de_frame(entrada: bytes, p: Parametros) -> np.ndarray:
    indice = p.entero("indice", 0, 10_000_000)
    p.exigir_sin_sobrantes()
    with tempfile.TemporaryDirectory(prefix="frame-") as directorio:
        # Ruta propia y fija: el nombre de la evidencia nunca llega a una ruta ni a FFmpeg.
        ruta = Path(directorio) / "entrada"
        ruta.write_bytes(entrada)
        captura = cv2.VideoCapture(str(ruta), cv2.CAP_FFMPEG, [cv2.CAP_PROP_N_THREADS, 1])
        try:
            if not captura.isOpened():
                raise ParametrosInvalidos("El contenido no es un video que OpenCV pueda leer.")
            # En orden desde el principio, sin seek: el seek no garantiza caer en el mismo frame.
            for leidos in range(indice):
                if not captura.grab():
                    raise ParametrosInvalidos(
                        f"El video tiene {leidos} frames; no existe el índice {indice}."
                    )
            ok, frame = captura.read()
            if not ok:
                raise ParametrosInvalidos(f"El video tiene {indice} frames; no existe el índice {indice}.")
            return frame
        finally:
            captura.release()


# Operaciones cuya entrada es un video (los bytes tal cual), no una imagen.
CATALOGO_DE_VIDEO: dict[str, Callable[[bytes, Parametros], np.ndarray]] = {
    "opencv.frame/v1": extraccion_de_frame,
}


def aplicar(operacion: str, parametros: str, pixeles: np.ndarray) -> np.ndarray:
    if operacion not in CATALOGO:
        raise ParametrosInvalidos(f"La operación '{operacion}' no está en el catálogo de imagen.")
    return CATALOGO[operacion](pixeles, Parametros(operacion, parametros))


def aplicar_a_bytes(operacion: str, parametros: str, entrada: bytes) -> np.ndarray:
    """Las de video reciben los bytes; las de imagen, la imagen decodificada sin transformaciones."""
    if operacion in CATALOGO_DE_VIDEO:
        return CATALOGO_DE_VIDEO[operacion](entrada, Parametros(operacion, parametros))
    if operacion not in CATALOGO:
        raise ParametrosInvalidos(f"La operación '{operacion}' no está en el catálogo.")
    return aplicar(operacion, parametros, decodificar(entrada))
