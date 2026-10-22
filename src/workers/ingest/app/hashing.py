"""Hash SHA-256 en streaming mientras el contenido se escribe a un archivo temporal."""

import hashlib
from collections.abc import AsyncIterable
from dataclasses import dataclass
from pathlib import Path


@dataclass(frozen=True)
class ContenidoHasheado:
    sha256: str
    tamano_bytes: int


async def escribir_y_hashear(trozos: AsyncIterable[bytes], destino: Path) -> ContenidoHasheado:
    """Escribe `trozos` en `destino` (que no debe existir) calculando el SHA-256 al vuelo."""
    sha = hashlib.sha256()
    tamano = 0
    with destino.open("xb") as f:
        async for trozo in trozos:
            if not trozo:
                continue
            sha.update(trozo)
            f.write(trozo)
            tamano += len(trozo)
    return ContenidoHasheado(sha256=sha.hexdigest(), tamano_bytes=tamano)
