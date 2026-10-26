"""Worker de IA: sólo investigación.

Corre en la workstation del analista y sólo lo llama el cliente local. Lo que produce nunca es
prueba: el servidor lo asienta como «sólo investigación» con la procedencia que este worker declara.
"""

import hashlib
import os
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from pathlib import Path
from typing import Annotated, Any

from fastapi import FastAPI, File, Form, HTTPException
from fastapi.concurrency import run_in_threadpool

from app import deteccion, entorno, modelos
from app.imagen import ImagenInvalida, a_bgr8, decodificar

TAMANO_MAXIMO_BYTES = 512 * 1024 * 1024


def crear_app(directorio_modelos: Path) -> FastAPI:
    estado: dict[str, Any] = {}

    @asynccontextmanager
    async def ciclo_de_vida(_: FastAPI) -> AsyncIterator[None]:
        # Sin todos los pesos verificados, el worker no arranca.
        verificados = modelos.verificar(modelos.MANIFIESTO, directorio_modelos)
        estado["sesiones"] = {
            id_: (modelo, modelos.abrir_sesion(modelo)) for id_, modelo in verificados.items()
        }
        yield
        estado.clear()

    app = FastAPI(title="Worker de IA forense", version=entorno.VERSION_WORKER, lifespan=ciclo_de_vida)

    @app.get("/v1/entorno")
    def ver_entorno() -> dict[str, Any]:
        return {
            "entorno": entorno.descripcion(),
            "modelos": {
                id_: {
                    "version": modelo.version,
                    "sha256": modelo.sha256,
                    "licencia": modelo.licencia,
                    "proveedor": modelos.proveedor_efectivo(sesion),
                }
                for id_, (modelo, sesion) in estado["sesiones"].items()
            },
        }

    def _detectar(umbral: int, contenido: bytes) -> dict[str, Any]:
        modelo, sesion = estado["sesiones"][deteccion.MODELO]
        bgr = a_bgr8(decodificar(contenido))
        hallazgos = deteccion.detectar(sesion, bgr, umbral)
        return {
            "modelo": {"id": modelo.id, "version": modelo.version, "sha256": modelo.sha256},
            "entrada": {"ancho": bgr.shape[1], "alto": bgr.shape[0]},
            "entorno": {
                **entorno.descripcion(),
                "proveedor": modelos.proveedor_efectivo(sesion),
                "postproceso": deteccion.POSTPROCESO,
            },
            "hallazgos": hallazgos,
        }

    @app.post("/v1/detectar")
    async def detectar(
        parametros: Annotated[str, Form()],
        entrada: Annotated[bytes, File()],
    ) -> dict[str, Any]:
        """Detección de objetos: pistas con región, clase, confianza y el dato crudo del modelo.

        El hash de la entrada es el de los bytes recibidos: el servidor lo cruza con el de la evidencia.
        """
        if len(entrada) > TAMANO_MAXIMO_BYTES:
            raise HTTPException(413, "La entrada supera el tamaño máximo.")
        try:
            umbral, canonicos = deteccion.interpretar_parametros(parametros)
            resultado = await run_in_threadpool(_detectar, umbral, entrada)
        except (ImagenInvalida, deteccion.ParametrosInvalidos) as ex:
            raise HTTPException(422, str(ex)) from ex
        resultado["entrada"] = {"sha256": hashlib.sha256(entrada).hexdigest(), **resultado["entrada"]}
        return {
            "analisis": deteccion.ANALISIS,
            "parametros": {"umbralConfianzaMilesimas": umbral},
            "parametrosCanonicos": canonicos,
            **resultado,
        }

    return app


app = crear_app(Path(os.environ.get("FORENSIC_IA_MODELOS", "/modelos")))
