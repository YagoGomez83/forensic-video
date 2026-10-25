"""Catálogo de operaciones deterministas. Espejo del CatalogoDeOperaciones del dominio.

La entrada no es confiable: los parámetros se validan acá otra vez (tipos exactos, sin campos de más)
y contra las dimensiones reales de la imagen, que el servidor no conoce.
"""

import json
from collections.abc import Callable
from typing import Any

import numpy as np

LADO_MAXIMO = 65_535


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


CATALOGO: dict[str, Callable[[np.ndarray, Parametros], np.ndarray]] = {
    "opencv.recorte/v1": recorte,
}


def aplicar(operacion: str, parametros: str, pixeles: np.ndarray) -> np.ndarray:
    if operacion not in CATALOGO:
        raise ParametrosInvalidos(f"La operación '{operacion}' no está en el catálogo.")
    return CATALOGO[operacion](pixeles, Parametros(operacion, parametros))
