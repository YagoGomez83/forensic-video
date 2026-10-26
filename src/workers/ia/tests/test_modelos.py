import hashlib
import json
from pathlib import Path

import numpy as np
import pytest

from app import modelos


def _manifiesto(tmp_path: Path, contenido: bytes, sha256: str | None = None) -> Path:
    (tmp_path / "pesos.onnx").write_bytes(contenido)
    manifiesto = tmp_path / "modelos.json"
    entrada = {
        "id": "prueba",
        "version": "1",
        "archivo": "pesos.onnx",
        "sha256": sha256 or hashlib.sha256(contenido).hexdigest(),
        "licencia": "Apache-2.0",
        "origen": "https://example.invalid/pesos.onnx",
    }
    manifiesto.write_text(json.dumps({"modelos": [entrada]}), encoding="utf-8")
    return manifiesto


def test_verificar_acepta_pesos_con_el_hash_del_manifiesto(tmp_path):
    manifiesto = _manifiesto(tmp_path, b"pesos")

    verificados = modelos.verificar(manifiesto, tmp_path)

    assert verificados["prueba"].sha256 == hashlib.sha256(b"pesos").hexdigest()
    assert verificados["prueba"].ruta == tmp_path / "pesos.onnx"


def test_verificar_rechaza_pesos_alterados(tmp_path):
    manifiesto = _manifiesto(tmp_path, b"pesos alterados", sha256=hashlib.sha256(b"pesos").hexdigest())

    with pytest.raises(modelos.ModeloInvalido, match="no coincide"):
        modelos.verificar(manifiesto, tmp_path)


def test_verificar_rechaza_pesos_ausentes_en_vez_de_descargarlos(tmp_path):
    manifiesto = _manifiesto(tmp_path, b"pesos")
    (tmp_path / "pesos.onnx").unlink()

    with pytest.raises(modelos.ModeloInvalido, match="no está"):
        modelos.verificar(manifiesto, tmp_path)


def test_los_pesos_reales_coinciden_con_el_manifiesto_del_worker(directorio_modelos):
    verificados = modelos.verificar(modelos.MANIFIESTO, directorio_modelos)

    assert "yolox-s" in verificados


def test_el_modelo_de_deteccion_corre_y_reporta_el_proveedor_efectivo(directorio_modelos):
    verificados = modelos.verificar(modelos.MANIFIESTO, directorio_modelos)
    sesion = modelos.abrir_sesion(verificados["yolox-s"])

    salida = sesion.run(None, {"images": np.zeros((1, 3, 640, 640), dtype=np.float32)})

    assert salida[0].shape == (1, 8400, 85)
    assert modelos.proveedor_efectivo(sesion) in ("CUDAExecutionProvider", "CPUExecutionProvider")


def test_con_cuda_disponible_se_precargan_sus_bibliotecas_antes_de_abrir_la_sesion(
    monkeypatch, directorio_modelos
):
    # Las bibliotecas de CUDA y cuDNN entran por pip (nvidia-*): sin precargarlas, onnxruntime no las
    # encuentra y cae a CPU en silencio aun con GPU.
    llamadas = []
    monkeypatch.setattr(
        modelos.ort, "get_available_providers", lambda: ["CUDAExecutionProvider", "CPUExecutionProvider"]
    )
    monkeypatch.setattr(modelos.ort, "preload_dlls", lambda **_: llamadas.append("precarga"))
    original = modelos.ort.InferenceSession
    monkeypatch.setattr(
        modelos.ort,
        "InferenceSession",
        lambda ruta, providers: (
            llamadas.append("sesion") or original(ruta, providers=["CPUExecutionProvider"])
        ),
    )
    modelos._precargar_cuda.cache_clear()
    verificados = modelos.verificar(modelos.MANIFIESTO, directorio_modelos)

    modelos.abrir_sesion(verificados["yolox-s"])
    modelos.abrir_sesion(verificados["yolox-s"])

    assert llamadas == ["precarga", "sesion", "sesion"]
