"""Sólo para desarrollo y CI: baja los pesos del manifiesto a ./modelos y verifica su hash.

El worker nunca descarga nada: en la imagen los pesos entran al construir (ADD --checksum) y en
desarrollo los deja este script. Un hash que no coincide borra el archivo y falla.
"""

import hashlib
import json
import sys
import urllib.request
from pathlib import Path

RAIZ = Path(__file__).resolve().parent.parent


def main() -> int:
    destino = RAIZ / "modelos"
    destino.mkdir(exist_ok=True)
    manifiesto = json.loads((RAIZ / "app" / "modelos.json").read_text(encoding="utf-8"))
    for entrada in manifiesto["modelos"]:
        ruta = destino / entrada["archivo"]
        if not ruta.is_file():
            print(f"Bajando {entrada['id']} desde {entrada['origen']}")
            urllib.request.urlretrieve(entrada["origen"], ruta)  # noqa: S310 - URL fija del manifiesto
        real = hashlib.sha256(ruta.read_bytes()).hexdigest()
        if real != entrada["sha256"]:
            ruta.unlink()
            print(f"El hash de {entrada['id']} no coincide ({real}); se borró.", file=sys.stderr)
            return 1
        print(f"{entrada['id']}: ok")
    return 0


if __name__ == "__main__":
    sys.exit(main())
