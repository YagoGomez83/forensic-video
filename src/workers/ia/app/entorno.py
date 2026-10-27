"""Entorno declarado por la workstation: va al asiento como procedencia, no como garantía.

La salida de un modelo en GPU no es reproducible byte a byte; lo que sí se puede es dejar
constancia de con qué se obtuvo (versiones, proveedor y GPU).
"""

import platform
import shutil
import subprocess  # noqa: S404 - nvidia-smi con argumentos fijos, sin entrada externa
import sys
from typing import Any

import cv2
import numpy as np
import onnxruntime as ort

VERSION_WORKER = "0.1.0"


def descripcion() -> dict[str, Any]:
    return {
        "worker": VERSION_WORKER,
        "python": platform.python_version(),
        "numpy": np.__version__,
        "opencv": cv2.__version__,
        "onnxruntime": ort.__version__,
        "plataforma": f"{sys.platform}-{platform.machine()}",
        "gpu": _gpu(),
    }


def _gpu() -> list[dict[str, str]] | None:
    """GPUs NVIDIA visibles (nombre, driver, memoria), o None si no hay ninguna."""
    nvidia_smi = shutil.which("nvidia-smi")
    if nvidia_smi is None:
        return None
    try:
        salida = subprocess.run(  # noqa: S603
            [nvidia_smi, "--query-gpu=name,driver_version,memory.total", "--format=csv,noheader"],
            capture_output=True,
            text=True,
            timeout=10,
            check=True,
        ).stdout
    except (subprocess.SubprocessError, OSError):
        return None
    gpus = []
    for linea in salida.strip().splitlines():
        nombre, driver, memoria = (c.strip() for c in linea.split(","))
        gpus.append({"nombre": nombre, "driver": driver, "memoria": memoria})
    return gpus or None
