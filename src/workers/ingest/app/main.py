"""Worker de ingesta.

Calcula el SHA-256 y extrae metadatos de contenedor/códec de un archivo. Corre en la workstation
y no se expone fuera de ella. Su hash es un control cruzado: la fuente de verdad es el hash que
calcula el servidor de custodia al ingestar, nunca el que reporta un worker.
"""

import tempfile
from pathlib import Path
from typing import Any

from fastapi import FastAPI, HTTPException, Request
from fastapi.concurrency import run_in_threadpool

from app import ffprobe
from app.hashing import escribir_y_hashear

app = FastAPI(title="Worker de ingesta forense", version="0.1.0")


@app.get("/salud")
def salud() -> dict[str, Any]:
    binario = ffprobe.ruta_ffprobe()
    return {
        "estado": "ok",
        "ffprobe": ffprobe.version(binario) if binario else None,
    }


@app.post("/v1/analizar")
async def analizar(request: Request) -> dict[str, Any]:
    """Recibe el archivo en crudo (application/octet-stream) y devuelve hash y metadatos."""
    binario = ffprobe.ruta_ffprobe()
    if binario is None:
        raise HTTPException(503, "ffprobe no está instalado en el worker.")

    with tempfile.TemporaryDirectory(prefix="ingesta-") as directorio:
        temporal = Path(directorio) / "contenido"
        hasheado = await escribir_y_hashear(request.stream(), temporal)
        if hasheado.tamano_bytes == 0:
            raise HTTPException(422, "Una evidencia vacía no es admisible.")
        try:
            metadatos = await run_in_threadpool(ffprobe.analizar, binario, temporal)
        except ffprobe.FfprobeError as ex:
            raise HTTPException(422, str(ex)) from ex

    return {
        "sha256": hasheado.sha256,
        "tamanoBytes": hasheado.tamano_bytes,
        "ffprobe": {
            "version": metadatos.version_ffprobe,
            "formato": metadatos.formato,
            "streams": metadatos.streams,
        },
    }
