"""Pesos clavados: cada modelo está en la imagen y se verifica por SHA-256 al arrancar.

Nada se descarga en caliente. Un peso ausente o alterado es un error de arranque, no un aviso: el
worker no sirve con un modelo que no sea exactamente el del manifiesto, porque su hash es la
procedencia que va al asiento.
"""

import functools
import hashlib
import json
from dataclasses import dataclass
from pathlib import Path

import onnxruntime as ort

MANIFIESTO = Path(__file__).with_name("modelos.json")

_BLOQUE = 1024 * 1024

# El worker corre aislado: nada sale de la workstation, tampoco la telemetría del runtime.
ort.disable_telemetry_events()


class ModeloInvalido(Exception):
    """El peso no está o no es el del manifiesto."""


@dataclass(frozen=True)
class Modelo:
    id: str
    version: str
    sha256: str
    licencia: str
    ruta: Path


def verificar(manifiesto: Path, directorio: Path) -> dict[str, Modelo]:
    """Verifica cada peso del manifiesto contra su hash, leyendo los bytes del disco."""
    verificados: dict[str, Modelo] = {}
    for entrada in json.loads(manifiesto.read_text(encoding="utf-8"))["modelos"]:
        ruta = directorio / entrada["archivo"]
        if not ruta.is_file():
            raise ModeloInvalido(f"El peso de '{entrada['id']}' no está en {directorio}.")
        real = _sha256(ruta)
        if real != entrada["sha256"]:
            raise ModeloInvalido(
                f"El hash del peso de '{entrada['id']}' no coincide con el manifiesto: {real}."
            )
        verificados[entrada["id"]] = Modelo(
            entrada["id"], entrada["version"], real, entrada["licencia"], ruta
        )
    return verificados


def abrir_sesion(modelo: Modelo) -> ort.InferenceSession:
    """Usa CUDA si la hay; si no, CPU. El proveedor que quedó se lee de la sesión, no de la lista."""
    proveedores = ["CPUExecutionProvider"]
    if "CUDAExecutionProvider" in ort.get_available_providers():
        _precargar_cuda()
        proveedores.insert(0, "CUDAExecutionProvider")
    return ort.InferenceSession(str(modelo.ruta), providers=proveedores)


@functools.cache
def _precargar_cuda() -> None:
    """CUDA y cuDNN entran por pip (paquetes nvidia-*): sin precargarlas, onnxruntime no las encuentra y
    cae a CPU en silencio aun con GPU. El driver (libcuda) lo pone nvidia-container-toolkit con --gpus."""
    ort.preload_dlls()


def proveedor_efectivo(sesion: ort.InferenceSession) -> str:
    return sesion.get_providers()[0]


def _sha256(ruta: Path) -> str:
    h = hashlib.sha256()
    with ruta.open("rb") as f:
        while bloque := f.read(_BLOQUE):
            h.update(bloque)
    return h.hexdigest()
