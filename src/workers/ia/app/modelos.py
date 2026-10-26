"""Pesos clavados: cada modelo está en la imagen y se verifica por SHA-256 al arrancar.

Nada se descarga en caliente. Un peso ausente o alterado es un error de arranque, no un aviso: el
worker no sirve con un modelo que no sea exactamente el del manifiesto, porque su hash es la
procedencia que va al asiento.

Un modelo puede ser compuesto (el OCR: detección, reconocimiento y diccionario): cada componente se
verifica por su hash, y el modelo tiene además un hash propio, el de la lista canónica de sus
componentes (ver hash_compuesto). Ese es el que va al asiento y el que conoce la lista blanca del
servidor: cambiar cualquier componente lo cambia.
"""

import functools
import hashlib
import json
from dataclasses import dataclass, field
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
    # Un modelo simple tiene ruta; uno compuesto, sus componentes (archivo -> ruta) y ruta None.
    ruta: Path | None
    componentes: dict[str, Path] = field(default_factory=dict)
    # Hash de cada componente, ya verificado al arrancar (archivo -> sha256).
    hashes_componentes: dict[str, str] = field(default_factory=dict)


def hash_compuesto(componentes: dict[str, str]) -> str:
    """SHA-256 de «archivo sha256» por línea, ordenado por archivo."""
    lineas = "".join(f"{archivo} {sha256}\n" for archivo, sha256 in sorted(componentes.items()))
    return hashlib.sha256(lineas.encode("utf-8")).hexdigest()


def verificar(manifiesto: Path, directorio: Path) -> dict[str, Modelo]:
    """Verifica cada peso del manifiesto contra su hash, leyendo los bytes del disco."""
    verificados: dict[str, Modelo] = {}
    for entrada in json.loads(manifiesto.read_text(encoding="utf-8"))["modelos"]:
        id_ = entrada["id"]
        if "componentes" in entrada:
            componentes = {
                c["archivo"]: _verificar_archivo(id_, directorio, c["archivo"], c["sha256"])
                for c in entrada["componentes"]
            }
            real = hash_compuesto({c["archivo"]: c["sha256"] for c in entrada["componentes"]})
            if real != entrada["sha256"]:
                raise ModeloInvalido(f"El hash compuesto de '{id_}' no coincide con el manifiesto: {real}.")
            hashes = {c["archivo"]: c["sha256"] for c in entrada["componentes"]}
            verificados[id_] = Modelo(
                id_, entrada["version"], real, entrada["licencia"], None, componentes, hashes
            )
        else:
            ruta = _verificar_archivo(id_, directorio, entrada["archivo"], entrada["sha256"])
            verificados[id_] = Modelo(id_, entrada["version"], entrada["sha256"], entrada["licencia"], ruta)
    return verificados


def _verificar_archivo(id_: str, directorio: Path, archivo: str, sha256: str) -> Path:
    ruta = directorio / archivo
    if not ruta.is_file():
        raise ModeloInvalido(f"El peso {archivo} de '{id_}' no está en {directorio}.")
    if (real := _sha256(ruta)) != sha256:
        raise ModeloInvalido(f"El hash del peso {archivo} de '{id_}' no coincide con el manifiesto: {real}.")
    return ruta


def abrir_sesion(modelo: Modelo, componente: str | None = None) -> ort.InferenceSession:
    """Usa CUDA si la hay; si no, CPU. El proveedor que quedó se lee de la sesión, no de la lista."""
    ruta = modelo.componentes[componente] if componente else modelo.ruta
    proveedores = ["CPUExecutionProvider"]
    if "CUDAExecutionProvider" in ort.get_available_providers():
        _precargar_cuda()
        proveedores.insert(0, "CUDAExecutionProvider")
    return ort.InferenceSession(str(ruta), providers=proveedores)


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
