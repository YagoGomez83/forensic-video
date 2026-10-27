"""Exporta Real-ESRGAN x4plus (BSD-3-Clause, xinntao/Real-ESRGAN v0.1.0) de .pth a ONNX.

Se corre una vez, fuera de la imagen del worker, en un entorno con exportacion/requisitos.txt:

    python exportacion/exportar_realesrgan.py <directorio de salida>

1. Baja el .pth original de su release y verifica su SHA-256 (el observado al fijar esta versión).
2. Lo carga con torch.load(weights_only=True): el .pth es un pickle, y así no ejecuta código.
3. Arma la RRDBNet (arquitectura de BasicSR, escrita acá para no depender del paquete) y exporta
   a ONNX con alto y ancho dinámicos.
4. Verifica que onnxruntime y PyTorch den lo mismo sobre una entrada aleatoria, e imprime el
   SHA-256 del .onnx: ese es el que va a app/modelos.json, y el archivo se publica en el release.
"""

import hashlib
import sys
import urllib.request
from pathlib import Path

import numpy as np
import onnxruntime as ort
import torch
from torch import nn
from torch.nn import functional as F  # noqa: N812

ORIGEN = "https://github.com/xinntao/Real-ESRGAN/releases/download/v0.1.0/RealESRGAN_x4plus.pth"
SHA256_ORIGEN = "4fa0d38905f75ac06eb49a7951b426670021be3018265fd191d2125df9d682f1"
SALIDA = "realesrgan_x4plus.onnx"
OPSET = 17


class BloqueDenso(nn.Module):
    def __init__(self, f=64, g=32):
        super().__init__()
        self.conv1 = nn.Conv2d(f, g, 3, 1, 1)
        self.conv2 = nn.Conv2d(f + g, g, 3, 1, 1)
        self.conv3 = nn.Conv2d(f + 2 * g, g, 3, 1, 1)
        self.conv4 = nn.Conv2d(f + 3 * g, g, 3, 1, 1)
        self.conv5 = nn.Conv2d(f + 4 * g, f, 3, 1, 1)
        self.lrelu = nn.LeakyReLU(negative_slope=0.2, inplace=True)

    def forward(self, x):
        x1 = self.lrelu(self.conv1(x))
        x2 = self.lrelu(self.conv2(torch.cat((x, x1), 1)))
        x3 = self.lrelu(self.conv3(torch.cat((x, x1, x2), 1)))
        x4 = self.lrelu(self.conv4(torch.cat((x, x1, x2, x3), 1)))
        x5 = self.conv5(torch.cat((x, x1, x2, x3, x4), 1))
        return x5 * 0.2 + x


class RRDB(nn.Module):
    def __init__(self, f=64, g=32):
        super().__init__()
        self.rdb1 = BloqueDenso(f, g)
        self.rdb2 = BloqueDenso(f, g)
        self.rdb3 = BloqueDenso(f, g)

    def forward(self, x):
        return self.rdb3(self.rdb2(self.rdb1(x))) * 0.2 + x


class RRDBNet(nn.Module):
    """Escala x4, 23 bloques, 64 filtros: la configuración de RealESRGAN_x4plus."""

    def __init__(self, f=64, bloques=23, g=32):
        super().__init__()
        self.conv_first = nn.Conv2d(3, f, 3, 1, 1)
        self.body = nn.Sequential(*[RRDB(f, g) for _ in range(bloques)])
        self.conv_body = nn.Conv2d(f, f, 3, 1, 1)
        self.conv_up1 = nn.Conv2d(f, f, 3, 1, 1)
        self.conv_up2 = nn.Conv2d(f, f, 3, 1, 1)
        self.conv_hr = nn.Conv2d(f, f, 3, 1, 1)
        self.conv_last = nn.Conv2d(f, 3, 3, 1, 1)
        self.lrelu = nn.LeakyReLU(negative_slope=0.2, inplace=True)

    def forward(self, x):
        rasgos = self.conv_first(x)
        rasgos = rasgos + self.conv_body(self.body(rasgos))
        rasgos = self.lrelu(self.conv_up1(F.interpolate(rasgos, scale_factor=2, mode="nearest")))
        rasgos = self.lrelu(self.conv_up2(F.interpolate(rasgos, scale_factor=2, mode="nearest")))
        return self.conv_last(self.lrelu(self.conv_hr(rasgos)))


def _sha256(ruta: Path) -> str:
    return hashlib.sha256(ruta.read_bytes()).hexdigest()


def main() -> int:
    salida = Path(sys.argv[1])
    salida.mkdir(parents=True, exist_ok=True)
    pth = salida / Path(ORIGEN).name
    if not pth.is_file():
        print(f"Bajando {ORIGEN}")
        urllib.request.urlretrieve(ORIGEN, pth)  # noqa: S310 - URL fija
    if (real := _sha256(pth)) != SHA256_ORIGEN:
        print(f"El .pth no es el esperado: {real}", file=sys.stderr)
        return 1

    estado = torch.load(pth, map_location="cpu", weights_only=True)
    modelo = RRDBNet()
    modelo.load_state_dict(estado["params_ema"], strict=True)
    modelo.eval()

    onnx = salida / SALIDA
    ejemplo = torch.rand(1, 3, 64, 64)
    torch.onnx.export(
        modelo,
        ejemplo,
        str(onnx),
        input_names=["entrada"],
        output_names=["salida"],
        dynamic_axes={"entrada": {2: "alto", 3: "ancho"}, "salida": {2: "alto4", 3: "ancho4"}},
        opset_version=OPSET,
        dynamo=False,
    )

    prueba = torch.rand(1, 3, 48, 40, generator=torch.Generator().manual_seed(0))
    with torch.no_grad():
        esperado = modelo(prueba).numpy()
    sesion = ort.InferenceSession(str(onnx), providers=["CPUExecutionProvider"])
    (obtenido,) = sesion.run(None, {"entrada": prueba.numpy()})
    diferencia = float(np.abs(esperado - obtenido).max())
    print(f"forma {obtenido.shape}, diferencia máxima ONNX vs PyTorch: {diferencia:.2e}")
    if obtenido.shape != (1, 3, 192, 160) or diferencia > 1e-3:
        print("La exportación no coincide con PyTorch.", file=sys.stderr)
        return 1
    print(f"{SALIDA}: sha256 {_sha256(onnx)}, {onnx.stat().st_size} bytes")
    return 0


if __name__ == "__main__":
    sys.exit(main())
