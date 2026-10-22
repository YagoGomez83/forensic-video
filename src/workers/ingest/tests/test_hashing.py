import asyncio
import hashlib
from pathlib import Path

import pytest

from app.hashing import escribir_y_hashear


async def _trozos(*partes: bytes):
    for p in partes:
        yield p


def test_hashea_en_streaming_igual_que_de_una_vez(tmp_path: Path):
    partes = [b"cabecera", b"", b"x" * 100_000, b"cola"]
    destino = tmp_path / "salida"

    r = asyncio.run(escribir_y_hashear(_trozos(*partes), destino))

    contenido = b"".join(partes)
    assert r.sha256 == hashlib.sha256(contenido).hexdigest()
    assert r.tamano_bytes == len(contenido)
    assert destino.read_bytes() == contenido


def test_no_pisa_un_archivo_existente(tmp_path: Path):
    destino = tmp_path / "salida"
    destino.write_bytes(b"previo")

    with pytest.raises(FileExistsError):
        asyncio.run(escribir_y_hashear(_trozos(b"nuevo"), destino))
    assert destino.read_bytes() == b"previo"
