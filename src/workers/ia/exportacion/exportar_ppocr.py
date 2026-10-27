"""Exporta PP-OCRv4 (Apache-2.0, PaddlePaddle/PaddleOCR) a ONNX: detección de texto y reconocimiento.

Se corre una vez, fuera de la imagen del worker, en un entorno con exportacion/requisitos-paddle.txt:

    python exportacion/exportar_ppocr.py <directorio de salida>

1. Baja los modelos de inferencia oficiales (.tar) y el diccionario, y verifica su SHA-256 (los
   observados al fijar esta versión).
2. Extrae los .tar sin salir del destino (filter="data"). Los modelos de Paddle son protobuf: leerlos
   no ejecuta código.
3. Convierte a ONNX con paddle2onnx (opset 14) e imprime el SHA-256 de cada archivo: esos van a
   app/modelos.json, y los archivos se publican en el release.
"""

import hashlib
import shutil
import sys
import tarfile
import urllib.request
from pathlib import Path

import paddle2onnx

OPSET = 14

ORIGENES = {
    "ch_PP-OCRv4_det_infer.tar": (
        "https://paddleocr.bj.bcebos.com/PP-OCRv4/chinese/ch_PP-OCRv4_det_infer.tar",
        "5f7217e0a89612e2f80d62f3c99a8bf5f7ae9cdc1ffd706be7dde07765627edf",
    ),
    "en_PP-OCRv4_rec_infer.tar": (
        "https://paddleocr.bj.bcebos.com/PP-OCRv4/english/en_PP-OCRv4_rec_infer.tar",
        "d5ae153a6fbdacd5776543d3194c4258154ed6f83467d55de1cc345f7dd20322",
    ),
    "en_dict.txt": (
        "https://raw.githubusercontent.com/PaddlePaddle/PaddleOCR/release/2.7/ppocr/utils/en_dict.txt",
        "5662df9d2d03f0e8ca0d3b0649d6acbab904b6a14b3d3521463c71c37c668ce3",
    ),
}

SALIDAS = {
    "ch_PP-OCRv4_det_infer": "ppocrv4_det.onnx",
    "en_PP-OCRv4_rec_infer": "ppocrv4_rec_en.onnx",
}


def _sha256(ruta: Path) -> str:
    return hashlib.sha256(ruta.read_bytes()).hexdigest()


def main() -> int:
    salida = Path(sys.argv[1])
    salida.mkdir(parents=True, exist_ok=True)
    for nombre, (url, esperado) in ORIGENES.items():
        ruta = salida / nombre
        if not ruta.is_file():
            print(f"Bajando {url}")
            urllib.request.urlretrieve(url, ruta)  # noqa: S310 - URL fija
        if (real := _sha256(ruta)) != esperado:
            print(f"{nombre} no es el esperado: {real}", file=sys.stderr)
            return 1

    for modelo, onnx in SALIDAS.items():
        with tarfile.open(salida / f"{modelo}.tar") as tar:
            tar.extractall(salida, filter="data")
        destino = salida / onnx
        paddle2onnx.export(
            str(salida / modelo / "inference.pdmodel"),
            str(salida / modelo / "inference.pdiparams"),
            str(destino),
            opset_version=OPSET,
            enable_onnx_checker=True,
        )
        shutil.rmtree(salida / modelo)
        print(f"{onnx}: sha256 {_sha256(destino)}, {destino.stat().st_size} bytes")
    print(f"en_dict.txt: sha256 {_sha256(salida / 'en_dict.txt')}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
