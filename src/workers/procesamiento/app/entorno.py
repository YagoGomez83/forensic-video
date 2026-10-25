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

VERSION_WORKER = "0.1.0"


def configurar() -> None:
    """Se llama en el hilo que procesa: el uso de OpenCL es por hilo en OpenCV."""
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
        # Capacidades de CPU según OpenCV: base compilada, despachables (*) y ausentes en esta CPU (?).
        # Con useOptimized=False no deberían influir; se asientan para diagnosticar otra máquina.
        "cpu": cv2.getCPUFeaturesLine(),
    }


configurar()
