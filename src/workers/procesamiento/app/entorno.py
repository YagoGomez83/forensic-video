"""Entorno de ejecución clavado para que cada operación sea reproducible byte a byte.

Un solo hilo, sin OpenCL y sin las rutas optimizadas (IPP/SIMD) que OpenCV elige en tiempo de
ejecución según la CPU: en operaciones de punto flotante esas rutas pueden cambiar bits entre
máquinas. Lo que queda (versiones y CPU) se reporta en cada respuesta y va al asiento.
"""

import hashlib
import platform
import sys
from typing import Any

import cv2
import numpy as np

HILOS = 1

# Capacidades de CPU que OpenCV detecta (cv2.CPU_*). Con useOptimized=False no deberían influir;
# se asientan igual para diagnosticar una reproducción fallida en otra máquina.
_CAPACIDADES = ("SSE2", "SSE4_1", "SSE4_2", "AVX", "AVX2", "AVX_512F", "NEON")

VERSION_WORKER = "0.1.0"


def configurar() -> None:
    cv2.setNumThreads(HILOS)
    cv2.setUseOptimized(False)
    cv2.ocl.setUseOpenCL(False)


def descripcion() -> dict[str, Any]:
    """El entorno tal como queda en el asiento de cada operación."""
    return {
        "worker": VERSION_WORKER,
        "python": platform.python_version(),
        "numpy": np.__version__,
        "opencv": cv2.__version__,
        "opencvBuildSha256": hashlib.sha256(cv2.getBuildInformation().encode()).hexdigest(),
        "hilos": cv2.getNumThreads(),
        "optimizado": cv2.useOptimized(),
        "opencl": cv2.ocl.useOpenCL(),
        "plataforma": f"{sys.platform}-{platform.machine()}",
        "cpu": sorted(c for c in _CAPACIDADES if cv2.checkHardwareSupport(getattr(cv2, f"CPU_{c}", -1))),
    }


configurar()
