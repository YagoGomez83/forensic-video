"""OCR con PP-OCRv4 (ONNX): detección de texto (DB) y reconocimiento (CTC), como PaddleOCR.

Cada línea leída es un hallazgo: región, el texto que el modelo leyó, su confianza y el dato crudo
(polígono, puntajes y la probabilidad de cada carácter). «El modelo leyó ABC123 con 0,82», nunca «la
patente es ABC123». Para leer una región (la patente de un vehículo detectado), primero se recorta con
la operación determinista de la Fase 2 y se lee el derivado: el linaje lo deja registrado.

Diferencia con la referencia: la expansión de cada caja (unclip) se hace sobre el rectángulo mínimo en
vez de con pyclipper sobre el polígono. Para las cajas rectangulares que produce DB es equivalente, y
evita una dependencia nativa; queda declarado en POSTPROCESO.
"""

import math
from typing import Any

import cv2
import numpy as np

from app import modelos

ANALISIS = "ia.ocr/v1"
MODELO = "ppocrv4-en"
DETECCION = "ppocrv4_det.onnx"
RECONOCIMIENTO = "ppocrv4_rec_en.onnx"
DICCIONARIO = "ppocrv4_en_dict.txt"

LADO_MAXIMO = 960
UMBRAL_BINARIZACION = 0.3
UMBRAL_CAJA = 0.6
EXPANSION = 1.5
LADO_MINIMO = 3
MAXIMO_CAJAS = 1000
ALTO_RECONOCIMIENTO = 48
ANCHO_MINIMO_RECONOCIMIENTO = 320

_MEDIA = np.array([0.485, 0.456, 0.406], dtype=np.float32)
_DESVIO = np.array([0.229, 0.224, 0.225], dtype=np.float32)

# Constantes del método: van en el entorno de cada respuesta (y de ahí al asiento).
POSTPROCESO = {
    "ladoMaximo": LADO_MAXIMO,
    "umbralBinarizacion": "0.3",
    "umbralCaja": "0.6",
    "expansion": "1.5",
    "expansionSobre": "rectangulo-minimo",
    "altoReconocimiento": ALTO_RECONOCIMIENTO,
}


class Lector:
    """Las dos sesiones (detección y reconocimiento) y el diccionario de un modelo compuesto verificado."""

    def __init__(self, modelo: modelos.Modelo):
        self.modelo = modelo
        self._deteccion = modelos.abrir_sesion(modelo, DETECCION)
        self._reconocimiento = modelos.abrir_sesion(modelo, RECONOCIMIENTO)
        self._diccionario = modelo.componentes[DICCIONARIO].read_text(encoding="utf-8").splitlines()

    @property
    def proveedor(self) -> str:
        return modelos.proveedor_efectivo(self._deteccion)

    def leer(self, bgr: np.ndarray, umbral_milesimas: int) -> list[dict[str, Any]]:
        alto, ancho = bgr.shape[:2]
        hallazgos = []
        for caja, puntaje_caja in _ordenar(self._detectar(bgr)):
            linea = _recortar(bgr, caja)
            texto, confianza, caracteres = self._reconocer(linea)
            if not texto or confianza < umbral_milesimas / 1000:
                continue
            hallazgos.append(
                {
                    "region": _region(caja, ancho, alto),
                    "textoLeido": texto,
                    "confianza": confianza,
                    "crudo": {
                        "poligono": [[float(x), float(y)] for x, y in caja],
                        "puntajeDeteccion": puntaje_caja,
                        "puntajeReconocimiento": confianza,
                        "caracteres": caracteres,
                    },
                }
            )
        return hallazgos

    def _detectar(self, bgr: np.ndarray) -> list[tuple[np.ndarray, float]]:
        alto, ancho = bgr.shape[:2]
        razon = min(1.0, LADO_MAXIMO / max(alto, ancho))
        alto_r = max(round(alto * razon / 32) * 32, 32)
        ancho_r = max(round(ancho * razon / 32) * 32, 32)
        x = (cv2.resize(bgr, (ancho_r, alto_r)).astype(np.float32) / 255 - _MEDIA) / _DESVIO
        (mapa,) = self._deteccion.run(None, {"x": np.ascontiguousarray(x.transpose(2, 0, 1)[np.newaxis])})
        probabilidad = mapa[0, 0]
        mascara = (probabilidad > UMBRAL_BINARIZACION).astype(np.uint8) * 255
        contornos, _ = cv2.findContours(mascara, cv2.RETR_LIST, cv2.CHAIN_APPROX_SIMPLE)
        cajas = []
        for contorno in contornos[:MAXIMO_CAJAS]:
            caja, lado = _caja_minima(contorno)
            if lado < LADO_MINIMO:
                continue
            puntaje = _puntaje(probabilidad, caja)
            if puntaje < UMBRAL_CAJA:
                continue
            caja, lado = _expandir(caja)
            if lado < LADO_MINIMO + 2:
                continue
            caja[:, 0] = np.clip(caja[:, 0] * ancho / ancho_r, 0, ancho)
            caja[:, 1] = np.clip(caja[:, 1] * alto / alto_r, 0, alto)
            cajas.append((caja, puntaje))
        return cajas

    def _reconocer(self, linea: np.ndarray) -> tuple[str, float, list[dict[str, Any]]]:
        alto, ancho = linea.shape[:2]
        razon = ancho / max(alto, 1)
        ancho_total = int(ALTO_RECONOCIMIENTO * max(ANCHO_MINIMO_RECONOCIMIENTO / ALTO_RECONOCIMIENTO, razon))
        ancho_r = min(ancho_total, math.ceil(ALTO_RECONOCIMIENTO * razon))
        x = (cv2.resize(linea, (ancho_r, ALTO_RECONOCIMIENTO)).astype(np.float32) / 255 - 0.5) / 0.5
        entrada = np.zeros((1, 3, ALTO_RECONOCIMIENTO, ancho_total), dtype=np.float32)
        entrada[0, :, :, :ancho_r] = x.transpose(2, 0, 1)
        (probabilidades,) = self._reconocimiento.run(None, {"x": entrada})
        return decodificar_ctc(probabilidades[0], self._diccionario)


def decodificar_ctc(
    probabilidades: np.ndarray, diccionario: list[str]
) -> tuple[str, float, list[dict[str, Any]]]:
    """Índice 0 = blanco, 1..n = diccionario, el último = espacio. Se quitan repetidos y blancos; la
    confianza es el promedio de las probabilidades de los caracteres que quedan."""
    simbolos = ["", *diccionario, " "]
    indices = probabilidades.argmax(axis=1)
    maximos = probabilidades.max(axis=1)
    caracteres = []
    anterior = 0
    for indice, probabilidad in zip(indices.tolist(), maximos.tolist(), strict=True):
        if indice != anterior and indice != 0:
            caracteres.append({"caracter": simbolos[indice], "probabilidad": float(probabilidad)})
        anterior = indice
    texto = "".join(c["caracter"] for c in caracteres)
    confianza = float(np.mean([c["probabilidad"] for c in caracteres])) if caracteres else 0.0
    return texto, confianza, caracteres


def _caja_minima(contorno: np.ndarray) -> tuple[np.ndarray, float]:
    """Las 4 esquinas del rectángulo mínimo, en orden: arriba-izq., arriba-der., abajo-der., abajo-izq."""
    rectangulo = cv2.minAreaRect(contorno)
    puntos = sorted(cv2.boxPoints(rectangulo).tolist(), key=lambda p: p[0])
    izquierda = sorted(puntos[:2], key=lambda p: p[1])
    derecha = sorted(puntos[2:], key=lambda p: p[1])
    caja = np.array([izquierda[0], derecha[0], derecha[1], izquierda[1]], dtype=np.float32)
    return caja, min(rectangulo[1])


def _puntaje(probabilidad: np.ndarray, caja: np.ndarray) -> float:
    """Probabilidad media dentro del polígono (como box_score_fast de PaddleOCR)."""
    alto, ancho = probabilidad.shape
    x0, x1 = (
        int(np.clip(np.floor(caja[:, 0].min()), 0, ancho - 1)),
        int(np.clip(np.ceil(caja[:, 0].max()), 0, ancho - 1)),
    )
    y0, y1 = (
        int(np.clip(np.floor(caja[:, 1].min()), 0, alto - 1)),
        int(np.clip(np.ceil(caja[:, 1].max()), 0, alto - 1)),
    )
    mascara = np.zeros((y1 - y0 + 1, x1 - x0 + 1), dtype=np.uint8)
    cv2.fillPoly(mascara, [(caja - [x0, y0]).astype(np.int32)], 1)
    return float(cv2.mean(probabilidad[y0 : y1 + 1, x0 : x1 + 1], mascara)[0])


def _expandir(caja: np.ndarray) -> tuple[np.ndarray, float]:
    """Unclip: agranda la caja en área x EXPANSION / perímetro por lado (sobre el rectángulo mínimo)."""
    (cx, cy), (w, h), angulo = cv2.minAreaRect(caja)
    distancia = w * h * EXPANSION / (2 * (w + h)) if w + h > 0 else 0
    expandido = cv2.boxPoints(((cx, cy), (w + 2 * distancia, h + 2 * distancia), angulo))
    return _caja_minima(expandido.astype(np.float32))


def _ordenar(cajas: list[tuple[np.ndarray, float]]) -> list[tuple[np.ndarray, float]]:
    """De arriba hacia abajo y, en la misma línea (10 px), de izquierda a derecha."""
    ordenadas = sorted(cajas, key=lambda c: (c[0][0][1], c[0][0][0]))
    for i in range(len(ordenadas) - 1):
        for j in range(i, -1, -1):
            a, b = ordenadas[j + 1][0][0], ordenadas[j][0][0]
            if abs(a[1] - b[1]) < 10 and a[0] < b[0]:
                ordenadas[j], ordenadas[j + 1] = ordenadas[j + 1], ordenadas[j]
            else:
                break
    return ordenadas


def _recortar(bgr: np.ndarray, caja: np.ndarray) -> np.ndarray:
    """La línea enderezada por perspectiva; si queda muy vertical, se rota 90°."""
    ancho = int(max(np.linalg.norm(caja[0] - caja[1]), np.linalg.norm(caja[2] - caja[3])))
    alto = int(max(np.linalg.norm(caja[0] - caja[3]), np.linalg.norm(caja[1] - caja[2])))
    ancho, alto = max(ancho, 1), max(alto, 1)
    destino = np.array([[0, 0], [ancho, 0], [ancho, alto], [0, alto]], dtype=np.float32)
    matriz = cv2.getPerspectiveTransform(caja.astype(np.float32), destino)
    linea = cv2.warpPerspective(
        bgr, matriz, (ancho, alto), borderMode=cv2.BORDER_REPLICATE, flags=cv2.INTER_CUBIC
    )
    return np.rot90(linea) if alto / ancho >= 1.5 else linea


def _region(caja: np.ndarray, ancho: int, alto: int) -> dict[str, int]:
    x0, y0 = math.floor(max(0.0, float(caja[:, 0].min()))), math.floor(max(0.0, float(caja[:, 1].min())))
    x1, y1 = (
        math.ceil(min(float(ancho), float(caja[:, 0].max()))),
        math.ceil(min(float(alto), float(caja[:, 1].max()))),
    )
    return {"x": x0, "y": y0, "ancho": max(x1 - x0, 1), "alto": max(y1 - y0, 1)}
