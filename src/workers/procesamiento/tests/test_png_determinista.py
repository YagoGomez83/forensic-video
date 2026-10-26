"""Prueba de concepto: el mismo array de píxeles da siempre los mismos bytes de PNG.

Es la base del gate de la Fase 2. Si el encoding metiera variabilidad (timestamps, metadatos,
diferencias por hilos), ningún derivado sería reproducible byte a byte.
"""

import hashlib
import struct
import subprocess
import sys
import zlib

import cv2
import numpy as np
import pytest

from app import entorno
from app.png import codificar_png, decodificar, hash_de_pixeles

rng = np.random.default_rng(seed=20261025)

ARRAYS = {
    "gris_8": rng.integers(0, 256, (61, 83), dtype=np.uint8),
    "bgr_8": rng.integers(0, 256, (61, 83, 3), dtype=np.uint8),
    "bgra_8": rng.integers(0, 256, (61, 83, 4), dtype=np.uint8),
    "gris_16": rng.integers(0, 65536, (61, 83), dtype=np.uint16),
    "bgr_16": rng.integers(0, 65536, (61, 83, 3), dtype=np.uint16),
}


def chunks(png: bytes) -> list[str]:
    assert png[:8] == b"\x89PNG\r\n\x1a\n"
    tipos, i = [], 8
    while i < len(png):
        (largo,) = struct.unpack(">I", png[i : i + 4])
        tipo = png[i + 4 : i + 8]
        crc = struct.unpack(">I", png[i + 8 + largo : i + 12 + largo])[0]
        assert zlib.crc32(tipo + png[i + 8 : i + 8 + largo]) == crc
        tipos.append(tipo.decode("ascii"))
        i += 12 + largo
    return tipos


@pytest.mark.parametrize("nombre", ARRAYS)
def test_el_mismo_array_da_los_mismos_bytes_dos_veces(nombre: str):
    pixeles = ARRAYS[nombre]

    assert codificar_png(pixeles) == codificar_png(pixeles.copy())


@pytest.mark.parametrize("nombre", ARRAYS)
def test_decodificar_lo_codificado_devuelve_el_array_exacto(nombre: str):
    pixeles = ARRAYS[nombre]

    vuelta = decodificar(codificar_png(pixeles))

    assert vuelta.dtype == pixeles.dtype
    assert np.array_equal(vuelta, pixeles)


@pytest.mark.parametrize("nombre", ARRAYS)
def test_el_png_no_lleva_metadatos_variables(nombre: str):
    # Sólo los chunks críticos: sin tIME, tEXt, iTXt, pHYs ni nada que pueda variar entre corridas.
    # libpng parte los datos en varios IDAT; eso es determinista.
    tipos = chunks(codificar_png(ARRAYS[nombre]))
    assert tipos[0] == "IHDR" and tipos[-1] == "IEND"
    assert set(tipos[1:-1]) == {"IDAT"}


def test_la_cantidad_de_hilos_no_cambia_los_bytes():
    pixeles = ARRAYS["bgr_16"]
    referencia = codificar_png(pixeles)
    try:
        for hilos in (1, 2, 8):
            cv2.setNumThreads(hilos)
            assert codificar_png(pixeles) == referencia
    finally:
        entorno.configurar()


def test_otro_proceso_produce_los_mismos_bytes():
    pixeles = ARRAYS["bgra_8"]
    script = (
        "import sys, numpy as np\n"
        "from app.png import codificar_png\n"
        "a = np.frombuffer(sys.stdin.buffer.read(), dtype=np.uint8).reshape(61, 83, 4)\n"
        "sys.stdout.buffer.write(codificar_png(a))\n"
    )

    otro = subprocess.run(
        [sys.executable, "-c", script], input=pixeles.tobytes(), capture_output=True, check=True
    )

    assert hashlib.sha256(otro.stdout).hexdigest() == hashlib.sha256(codificar_png(pixeles)).hexdigest()


def test_el_entorno_queda_clavado():
    entorno.configurar()

    assert cv2.getNumThreads() == entorno.HILOS
    assert not cv2.useOptimized()
    assert not cv2.ocl.useOpenCL()


# Calculados con el entorno fijado y verificados idénticos en Windows y en la imagen Docker (Linux).
# Si fallan, cambió algo del entorno (OpenCV, numpy, zlib): ningún derivado previo reproduciría.
DORADOS = {
    "bgr_8": (
        "3b65dd33837b9e0e6a153187595f70027163c99331bc2ec0585a2b3a551d0f14",
        "c21ce4af91343bd9c2f1037b32babcff3602b8a142863091c9f17e5be9f18955",
    ),
    "gris_16": (
        "7e7d980e3bf9f54491043c260970d1f5fa4041d57cf04af54cc7ae7b032fc997",
        "eccef6c434c29a9bdff0773910009a0c79e8e5befa313670d78c0317ecd03651",
    ),
}


@pytest.mark.parametrize("nombre", DORADOS)
def test_los_bytes_coinciden_con_los_dorados_del_entorno_fijado(nombre: str):
    # Arrays regenerados con su propio generador para no depender del orden de ARRAYS.
    rng_dorado = np.random.default_rng(seed=20261025)
    arrays = {
        "bgr_8": rng_dorado.integers(0, 256, (61, 83, 3), dtype=np.uint8),
        "gris_16": rng_dorado.integers(0, 65536, (61, 83), dtype=np.uint16),
    }
    png_esperado, pixeles_esperado = DORADOS[nombre]

    assert hashlib.sha256(codificar_png(arrays[nombre])).hexdigest() == png_esperado
    assert hash_de_pixeles(arrays[nombre]) == pixeles_esperado


def test_el_entorno_reporta_la_cpu_real_aunque_las_rutas_optimizadas_esten_apagadas():
    # Con useOptimized=False OpenCV marca todo como ausente ("?"); el diagnóstico tiene que ser el real.
    entorno.configurar()
    capacidades = entorno.descripcion()["cpu"].split()

    assert capacidades
    assert not all(c.endswith("?") for c in capacidades)
