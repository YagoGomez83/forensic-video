"""Worker de IA: sólo investigación.

Corre en la workstation del analista y sólo lo llama el cliente local. Lo que produce nunca es
prueba: el servidor lo asienta como «sólo investigación» con la procedencia que este worker declara.
"""

import os
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from pathlib import Path
from typing import Any

from fastapi import FastAPI

from app import entorno, modelos


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

    return app


app = crear_app(Path(os.environ.get("FORENSIC_IA_MODELOS", "/modelos")))
