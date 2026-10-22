"""Metadatos de contenedor y códecs vía ffprobe.

ffprobe se invoca siempre con una lista de argumentos (nunca a través de un shell) y sobre una
ruta temporal generada por el worker: el nombre que trae la evidencia jamás llega a la línea de
comandos.
"""

import json
import shutil
import subprocess  # noqa: S404 — uso controlado, sin shell
from dataclasses import dataclass
from pathlib import Path
from typing import Any

TIMEOUT_SEGUNDOS = 120


class FfprobeError(Exception):
    pass


@dataclass(frozen=True)
class Metadatos:
    version_ffprobe: str
    formato: dict[str, Any]
    streams: list[dict[str, Any]]


def ruta_ffprobe() -> str | None:
    return shutil.which("ffprobe")


def version(binario: str) -> str:
    salida = _ejecutar([binario, "-version"])
    # Primera línea: "ffprobe version 7.1.1 Copyright ..."
    partes = salida.splitlines()[0].split() if salida else []
    return partes[2] if len(partes) > 2 and partes[1] == "version" else "desconocida"


def analizar(binario: str, archivo: Path) -> Metadatos:
    salida = _ejecutar(
        [
            binario,
            "-v",
            "error",
            "-print_format",
            "json",
            "-show_format",
            "-show_streams",
            str(archivo),
        ]
    )
    return interpretar(salida, version(binario))


def interpretar(salida_json: str, version_ffprobe: str) -> Metadatos:
    try:
        datos = json.loads(salida_json)
    except json.JSONDecodeError as ex:
        raise FfprobeError(f"ffprobe devolvió JSON inválido: {ex}") from ex
    formato = datos.get("format")
    if not isinstance(formato, dict):
        raise FfprobeError("ffprobe no reconoció el contenedor.")
    formato.pop("filename", None)  # ruta temporal del worker: no es un dato de la evidencia
    return Metadatos(version_ffprobe=version_ffprobe, formato=formato, streams=datos.get("streams", []))


def _ejecutar(args: list[str]) -> str:
    try:
        r = subprocess.run(  # noqa: S603 — args en lista, sin shell
            args, capture_output=True, text=True, timeout=TIMEOUT_SEGUNDOS, check=False
        )
    except subprocess.TimeoutExpired as ex:
        raise FfprobeError("ffprobe excedió el tiempo límite.") from ex
    if r.returncode != 0:
        raise FfprobeError(f"ffprobe terminó con código {r.returncode}: {r.stderr.strip()[:500]}")
    return r.stdout
