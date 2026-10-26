"""Worker de procesamiento.

Aplica operaciones deterministas de OpenCV y devuelve el derivado como PNG lossless codificado de
forma determinista. Sólo lo llama el servidor de custodia, que vuelve a hashear la salida: los
hashes que reporta este worker son un control cruzado, no la fuente de verdad.
"""

import hashlib
import json
from typing import Annotated, Any

from fastapi import FastAPI, File, Form, HTTPException, Response
from fastapi.concurrency import run_in_threadpool

from app import entorno, operaciones
from app.png import ImagenInvalida, codificar_png, hash_de_pixeles

TAMANO_MAXIMO_BYTES = 512 * 1024 * 1024

app = FastAPI(title="Worker de procesamiento forense", version=entorno.VERSION_WORKER)


def _entorno_compacto() -> str:
    return json.dumps(entorno.descripcion(), sort_keys=True, separators=(",", ":"), ensure_ascii=True)


@app.get("/salud")
def salud() -> dict[str, Any]:
    catalogo = sorted([*operaciones.CATALOGO, *operaciones.CATALOGO_DE_VIDEO])
    return {"estado": "ok", "entorno": entorno.descripcion(), "operaciones": catalogo}


def _procesar(operacion: str, parametros: str, entrada: bytes) -> tuple[bytes, str, str]:
    # En el mismo hilo que procesa (OpenCL es por hilo), y el entorno se lee desde ahí: es el real.
    entorno.configurar()
    salida = operaciones.aplicar_a_bytes(operacion, parametros, entrada)
    return codificar_png(salida), hash_de_pixeles(salida), _entorno_compacto()


@app.post("/v1/operaciones")
async def aplicar(
    operacion: Annotated[str, Form()],
    parametros: Annotated[str, Form()],
    entrada: Annotated[bytes, File()],
) -> Response:
    """Recibe la entrada en crudo y devuelve el derivado PNG, con los hashes y el entorno en cabeceras."""
    if len(entrada) > TAMANO_MAXIMO_BYTES:
        raise HTTPException(413, "La entrada supera el tamaño máximo.")
    try:
        png, sha_pixeles, entorno_real = await run_in_threadpool(_procesar, operacion, parametros, entrada)
    except (ImagenInvalida, operaciones.ParametrosInvalidos) as ex:
        raise HTTPException(422, str(ex)) from ex

    return Response(
        content=png,
        media_type="image/png",
        headers={
            "X-Operacion": operacion,
            "X-Sha256-Entrada": hashlib.sha256(entrada).hexdigest(),
            "X-Sha256-Salida": hashlib.sha256(png).hexdigest(),
            "X-Sha256-Pixeles": sha_pixeles,
            "X-Entorno": entorno_real,
        },
    )
