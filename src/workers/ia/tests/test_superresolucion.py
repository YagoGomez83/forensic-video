import numpy as np
import pytest

from app import modelos, superresolucion
from app.imagen import ImagenInvalida


@pytest.fixture(scope="module")
def sesion(directorio_modelos):
    verificados = modelos.verificar(modelos.MANIFIESTO, directorio_modelos)
    return modelos.abrir_sesion(verificados[superresolucion.MODELO])


def test_escala_por_cuatro_en_bgr_de_8_bits(sesion):
    bgr = np.random.default_rng(1).integers(0, 256, (24, 32, 3), dtype=np.uint8)

    salida = superresolucion.superresolver(sesion, bgr)

    assert salida.shape == (96, 128, 3)
    assert salida.dtype == np.uint8


def test_conserva_un_color_plano(sesion):
    # Un gris liso no tiene detalle que inventar: el modelo lo devuelve casi igual (y en BGR, no RGB).
    bgr = np.zeros((16, 16, 3), dtype=np.uint8)
    bgr[:, :] = (40, 120, 200)

    salida = superresolucion.superresolver(sesion, bgr)

    assert np.abs(salida.reshape(-1, 3).mean(axis=0) - (40, 120, 200)).max() < 8


def test_en_mosaicos_da_casi_lo_mismo_que_en_una_sola_pasada(sesion, monkeypatch):
    # Sobre un gradiente, un mosaico mal ubicado o sin escribir da una diferencia grande; las costuras,
    # apenas un poco. (Sobre ruido el margen de contexto pesa más y no discrimina tan bien.)
    filas, columnas = np.indices((20, 37))
    bgr = np.stack([columnas * 6, filas * 12, np.full_like(filas, 128)], axis=2).astype(np.uint8)
    entera = superresolucion.superresolver(sesion, bgr)
    monkeypatch.setattr(superresolucion, "MOSAICO", 16)

    en_mosaicos = superresolucion.superresolver(sesion, bgr)

    assert en_mosaicos.shape == entera.shape == (80, 148, 3)
    # Medido: costuras ~1,5; con la segunda columna de mosaicos corrida, ~16.
    assert np.abs(en_mosaicos.astype(int) - entera.astype(int)).mean() < 4


def test_en_cpu_da_lo_mismo_dos_veces(sesion):
    bgr = np.random.default_rng(3).integers(0, 256, (12, 12, 3), dtype=np.uint8)

    assert np.array_equal(
        superresolucion.superresolver(sesion, bgr), superresolucion.superresolver(sesion, bgr)
    )


def test_rechaza_entradas_demasiado_grandes(sesion, monkeypatch):
    monkeypatch.setattr(superresolucion, "MAXIMO_PIXELES_ENTRADA", 100)

    with pytest.raises(ImagenInvalida, match="píxeles"):
        superresolucion.superresolver(sesion, np.zeros((11, 10, 3), dtype=np.uint8))


def test_los_parametros_son_un_objeto_vacio():
    assert superresolucion.interpretar_parametros(" { } ") == "{}"
    for invalidos in ["", "[]", '{"escala": 4}', "no es json"]:
        with pytest.raises(superresolucion.ParametrosInvalidos):
            superresolucion.interpretar_parametros(invalidos)
