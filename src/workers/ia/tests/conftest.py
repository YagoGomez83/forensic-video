import os
from pathlib import Path

import pytest

RAIZ = Path(__file__).resolve().parent.parent


@pytest.fixture(scope="session")
def directorio_modelos() -> Path:
    """Los pesos reales, tal como quedan en la imagen. En desarrollo: scripts/descargar_modelos.py."""
    directorio = Path(os.environ.get("FORENSIC_IA_MODELOS", RAIZ / "modelos"))
    if not directorio.is_dir():
        pytest.fail(
            f"Faltan los pesos en {directorio}: correr `python scripts/descargar_modelos.py` "
            "(o definir FORENSIC_IA_MODELOS)."
        )
    return directorio
