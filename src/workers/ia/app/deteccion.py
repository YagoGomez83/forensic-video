"""Detección de objetos con YOLOX (ONNX): preprocesado, postprocesado y NMS.

Sigue la implementación de referencia de YOLOX: letterbox a 640 rellenando con 114, BGR sin
normalizar; cada ancla aporta su mejor clase con puntaje = objetividad x puntaje de la clase; NMS por
clase con IoU 0,45. El resultado son pistas: región, clase, confianza y el dato crudo del modelo.
"""

import json
import math
from typing import Any

import cv2
import numpy as np

ANALISIS = "ia.deteccion/v1"
MODELO = "yolox-s"

LADO = 640
RELLENO = 114
NMS_IOU = 0.45
PASOS = (8, 16, 32)
ANCLAS = sum((LADO // p) ** 2 for p in PASOS)

UMBRAL_MINIMO = 1
UMBRAL_MAXIMO = 999

# Constantes del método: van en el entorno de cada respuesta (y de ahí al asiento).
POSTPROCESO = {"lado": LADO, "relleno": RELLENO, "nmsIou": "0.45"}

CLASES_COCO = (
    "person", "bicycle", "car", "motorcycle", "airplane", "bus", "train", "truck", "boat",
    "traffic light", "fire hydrant", "stop sign", "parking meter", "bench", "bird", "cat", "dog",
    "horse", "sheep", "cow", "elephant", "bear", "zebra", "giraffe", "backpack", "umbrella",
    "handbag", "tie", "suitcase", "frisbee", "skis", "snowboard", "sports ball", "kite",
    "baseball bat", "baseball glove", "skateboard", "surfboard", "tennis racket", "bottle",
    "wine glass", "cup", "fork", "knife", "spoon", "bowl", "banana", "apple", "sandwich", "orange",
    "broccoli", "carrot", "hot dog", "pizza", "donut", "cake", "chair", "couch", "potted plant",
    "bed", "dining table", "toilet", "tv", "laptop", "mouse", "remote", "keyboard", "cell phone",
    "microwave", "oven", "toaster", "sink", "refrigerator", "book", "clock", "vase", "scissors",
    "teddy bear", "hair drier", "toothbrush",
)  # fmt: skip


class ParametrosInvalidos(ValueError):
    """Los parámetros no son los que admite la detección."""


def interpretar_parametros(texto: str) -> tuple[int, str]:
    """Devuelve el umbral en milésimas y los parámetros en JSON canónico (como los arma el servidor)."""
    try:
        parametros = json.loads(texto)
    except json.JSONDecodeError as ex:
        raise ParametrosInvalidos("Los parámetros no son JSON.") from ex
    if not isinstance(parametros, dict) or set(parametros) != {"umbralConfianzaMilesimas"}:
        raise ParametrosInvalidos("La detección admite sólo 'umbralConfianzaMilesimas'.")
    umbral = parametros["umbralConfianzaMilesimas"]
    if type(umbral) is not int or not UMBRAL_MINIMO <= umbral <= UMBRAL_MAXIMO:
        raise ParametrosInvalidos(
            f"'umbralConfianzaMilesimas' es un entero de {UMBRAL_MINIMO} a {UMBRAL_MAXIMO}."
        )
    return umbral, json.dumps({"umbralConfianzaMilesimas": umbral}, separators=(",", ":"))


def preprocesar(bgr: np.ndarray) -> tuple[np.ndarray, float]:
    """Letterbox: escala sin deformar hasta caber en LADO x LADO, arriba a la izquierda, y rellena."""
    alto, ancho = bgr.shape[:2]
    razon = min(LADO / alto, LADO / ancho)
    escalada = cv2.resize(
        bgr, (int(ancho * razon), int(alto * razon)), interpolation=cv2.INTER_LINEAR
    ).astype(np.uint8)
    lienzo = np.full((LADO, LADO, 3), RELLENO, dtype=np.uint8)
    lienzo[: escalada.shape[0], : escalada.shape[1]] = escalada
    tensor = np.ascontiguousarray(lienzo.transpose(2, 0, 1)[np.newaxis], dtype=np.float32)
    return tensor, razon


def _grillas() -> tuple[np.ndarray, np.ndarray]:
    grillas, pasos = [], []
    for paso in PASOS:
        lado = LADO // paso
        xv, yv = np.meshgrid(np.arange(lado), np.arange(lado))
        grillas.append(np.stack((xv, yv), 2).reshape(-1, 2))
        pasos.append(np.full((lado * lado, 1), paso))
    return np.concatenate(grillas).astype(np.float64), np.concatenate(pasos).astype(np.float64)


_GRILLAS, _PASOS = _grillas()


def postprocesar(
    salida: np.ndarray, razon: float, ancho: int, alto: int, umbral_milesimas: int
) -> list[dict[str, Any]]:
    """De la salida cruda [1, ANCLAS, 85] a hallazgos en píxeles de la imagen original."""
    p = salida[0].astype(np.float64)
    centros = (p[:, 0:2] + _GRILLAS) * _PASOS
    tamanos = np.exp(p[:, 2:4]) * _PASOS
    cajas = np.concatenate((centros - tamanos / 2, centros + tamanos / 2), axis=1) / razon
    objetividad = p[:, 4]
    clases = p[:, 5:].argmax(axis=1)
    puntaje_clase = p[np.arange(len(p)), 5 + clases]
    puntajes = objetividad * puntaje_clase

    candidatas = np.flatnonzero(puntajes >= umbral_milesimas / 1000)
    hallazgos = []
    for i in _nms_por_clase(cajas, puntajes, clases, candidatas):
        region = _region(cajas[i], ancho, alto)
        if region is None:
            continue
        puntaje = float(puntajes[i])
        hallazgos.append(
            {
                "region": region,
                "clase": CLASES_COCO[int(clases[i])],
                "confianza": puntaje,
                "crudo": {
                    "caja": [float(v) for v in cajas[i]],
                    "claseIndice": int(clases[i]),
                    "objetividad": float(objetividad[i]),
                    "puntajeClase": float(puntaje_clase[i]),
                    "puntaje": puntaje,
                },
            }
        )
    return hallazgos


def _nms_por_clase(
    cajas: np.ndarray, puntajes: np.ndarray, clases: np.ndarray, candidatas: np.ndarray
) -> list[int]:
    """Supresión de no máximos por clase. Orden estable: puntaje descendente, desempate por ancla."""
    conservadas: list[int] = []
    for clase in np.unique(clases[candidatas]):
        restantes = candidatas[clases[candidatas] == clase]
        restantes = restantes[np.lexsort((restantes, -puntajes[restantes]))]
        while restantes.size:
            mejor, restantes = restantes[0], restantes[1:]
            conservadas.append(int(mejor))
            restantes = restantes[_iou(cajas[mejor], cajas[restantes]) <= NMS_IOU]
    return sorted(conservadas, key=lambda i: (-puntajes[i], i))


def _iou(caja: np.ndarray, otras: np.ndarray) -> np.ndarray:
    """IoU de una caja contra varias, sin el «+1» histórico en las áreas."""
    ancho = np.clip(np.minimum(caja[2], otras[:, 2]) - np.maximum(caja[0], otras[:, 0]), 0, None)
    alto = np.clip(np.minimum(caja[3], otras[:, 3]) - np.maximum(caja[1], otras[:, 1]), 0, None)
    interseccion = ancho * alto
    area = (caja[2] - caja[0]) * (caja[3] - caja[1])
    areas = (otras[:, 2] - otras[:, 0]) * (otras[:, 3] - otras[:, 1])
    union = area + areas - interseccion
    return np.divide(interseccion, union, out=np.zeros_like(union), where=union > 0)


def _region(caja: np.ndarray, ancho: int, alto: int) -> dict[str, int] | None:
    """Envolvente entera de la caja, recortada a la imagen; None si cae entera fuera (en el relleno)."""
    x1, y1 = max(0.0, caja[0]), max(0.0, caja[1])
    x2, y2 = min(float(ancho), caja[2]), min(float(alto), caja[3])
    if x2 <= x1 or y2 <= y1:
        return None
    x, y = math.floor(x1), math.floor(y1)
    return {"x": x, "y": y, "ancho": math.ceil(x2) - x, "alto": math.ceil(y2) - y}


def detectar(sesion, bgr: np.ndarray, umbral_milesimas: int) -> list[dict[str, Any]]:
    tensor, razon = preprocesar(bgr)
    (salida,) = sesion.run(None, {"images": tensor})
    alto, ancho = bgr.shape[:2]
    return postprocesar(salida, razon, ancho, alto, umbral_milesimas)
