"""Baja los pesos del manifiesto (app/modelos.json) y verifica su hash. Sólo AL CONSTRUIR la imagen
(destino /modelos) y en desarrollo y CI (destino ./modelos); el worker nunca descarga nada.

Un hash que no coincide borra el archivo y falla: el build de la imagen se corta ahí.

    python scripts/descargar_modelos.py [destino]
"""

import hashlib
import json
import sys
import urllib.request
from pathlib import Path

RAIZ = Path(__file__).resolve().parent.parent


def main() -> int:
    destino = Path(sys.argv[1]) if len(sys.argv) > 1 else RAIZ / "modelos"
    destino.mkdir(parents=True, exist_ok=True)
    manifiesto = json.loads((RAIZ / "app" / "modelos.json").read_text(encoding="utf-8"))
    for entrada in manifiesto["modelos"]:
        # Un modelo compuesto (el OCR) baja cada componente; el hash compuesto lo verifica el worker.
        for archivo in entrada.get("componentes", [entrada]):
            ruta = destino / archivo["archivo"]
            if not ruta.is_file():
                print(f"Bajando {archivo['archivo']} desde {archivo['origen']}")
                urllib.request.urlretrieve(archivo["origen"], ruta)  # noqa: S310 - URL fija del manifiesto
            real = hashlib.sha256(ruta.read_bytes()).hexdigest()
            if real != archivo["sha256"]:
                ruta.unlink()
                print(f"El hash de {archivo['archivo']} no coincide ({real}); se borró.", file=sys.stderr)
                return 1
        print(f"{entrada['id']}: ok")
    return 0


if __name__ == "__main__":
    sys.exit(main())
