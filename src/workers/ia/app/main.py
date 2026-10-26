"""Worker de IA: sólo investigación.

Corre en la workstation del analista y sólo lo llama el cliente local. Lo que produce nunca es
prueba: el servidor lo asienta como «sólo investigación» con la procedencia que este worker declara.
"""

import hashlib
import json
import os
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from pathlib import Path
from typing import Annotated, Any

from fastapi import FastAPI, File, Form, HTTPException, Response
from fastapi.concurrency import run_in_threadpool

from app import deteccion, entorno, modelos, superresolucion
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

    def _superresolver(contenido: bytes) -> tuple[bytes, dict[str, Any]]:
        modelo, sesion = estado["sesiones"][superresolucion.MODELO]
        bgr = a_bgr8(decodificar(contenido))
        derivado = superresolucion.superresolver(sesion, bgr)
        png = superresolucion.codificar_png(derivado)
        return png, {
            "modelo": {"id": modelo.id, "version": modelo.version, "sha256": modelo.sha256},
            "entrada": {"ancho": bgr.shape[1], "alto": bgr.shape[0]},
            "salida": {
                "sha256": hashlib.sha256(png).hexdigest(),
                "ancho": derivado.shape[1],
                "alto": derivado.shape[0],
            },
            "entorno": {
                **entorno.descripcion(),
                "proveedor": modelos.proveedor_efectivo(sesion),
                "postproceso": superresolucion.POSTPROCESO,
            },
        }

    @app.post("/v1/superresolucion")
    async def superresolver(
        parametros: Annotated[str, Form()],
        entrada: Annotated[bytes, File()],
    ) -> Response:
        """Super-resolución x4: el derivado PNG en el cuerpo y su reporte (procedencia) en X-Reporte.

        El cliente reenvía al servidor el PNG y el reporte tal cual; el servidor vuelve a hashear el PNG.
        """
        if len(entrada) > TAMANO_MAXIMO_BYTES:
            raise HTTPException(413, "La entrada supera el tamaño máximo.")
        try:
            canonicos = superresolucion.interpretar_parametros(parametros)
            png, reporte = await run_in_threadpool(_superresolver, entrada)
        except (ImagenInvalida, superresolucion.ParametrosInvalidos) as ex:
            raise HTTPException(422, str(ex)) from ex
        reporte = {
            "operacion": superresolucion.OPERACION,
            "parametros": json.loads(canonicos),
            **reporte,
            "entrada": {"sha256": hashlib.sha256(entrada).hexdigest(), **reporte["entrada"]},
        }
        # ASCII y compacto: va en una cabecera HTTP.
        cabecera = json.dumps(reporte, separators=(",", ":"), ensure_ascii=True)
        return Response(content=png, media_type="image/png", headers={"X-Reporte": cabecera})

    return app


app = crear_app(Path(os.environ.get("FORENSIC_IA_MODELOS", "/modelos")))
