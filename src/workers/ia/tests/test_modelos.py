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


def _compuesto(tmp_path: Path, archivos: dict[str, bytes], sha256: str | None = None) -> Path:
    for nombre, contenido in archivos.items():
        (tmp_path / nombre).write_bytes(contenido)
    componentes = [
        {
            "archivo": nombre,
            "sha256": hashlib.sha256(contenido).hexdigest(),
            "origen": f"https://example.invalid/{nombre}",
        }
        for nombre, contenido in archivos.items()
    ]
    manifiesto = tmp_path / "modelos.json"
    entrada = {
        "id": "compuesto",
        "version": "1",
        "sha256": sha256 or modelos.hash_compuesto({c["archivo"]: c["sha256"] for c in componentes}),
        "licencia": "Apache-2.0",
        "componentes": componentes,
    }
    manifiesto.write_text(json.dumps({"modelos": [entrada]}), encoding="utf-8")
    return manifiesto


def test_el_hash_compuesto_es_el_de_la_lista_canonica_de_componentes():
    # SHA-256 de «archivo sha256» por línea, ordenado por archivo: cambiar cualquier componente lo cambia.
    esperado = hashlib.sha256(b"a.onnx " + b"1" * 64 + b"\nb.txt " + b"2" * 64 + b"\n").hexdigest()

    assert modelos.hash_compuesto({"b.txt": "2" * 64, "a.onnx": "1" * 64}) == esperado


def test_un_modelo_compuesto_verifica_cada_componente_y_su_hash(tmp_path):
    manifiesto = _compuesto(tmp_path, {"det.onnx": b"det", "dic.txt": b"dic"})

    modelo = modelos.verificar(manifiesto, tmp_path)["compuesto"]

    assert modelo.componentes == {"det.onnx": tmp_path / "det.onnx", "dic.txt": tmp_path / "dic.txt"}
    assert modelo.ruta is None


def test_un_componente_alterado_impide_arrancar(tmp_path):
    manifiesto = _compuesto(tmp_path, {"det.onnx": b"det", "dic.txt": b"dic"})
    (tmp_path / "dic.txt").write_bytes(b"otro diccionario")

    with pytest.raises(modelos.ModeloInvalido, match="dic.txt"):
        modelos.verificar(manifiesto, tmp_path)


def test_un_hash_compuesto_que_no_corresponde_impide_arrancar(tmp_path):
    manifiesto = _compuesto(tmp_path, {"det.onnx": b"det"}, sha256="0" * 64)

    with pytest.raises(modelos.ModeloInvalido, match="compuesto"):
        modelos.verificar(manifiesto, tmp_path)


def test_el_ocr_es_un_modelo_compuesto_en_el_manifiesto_real(directorio_modelos):
    ocr = modelos.verificar(modelos.MANIFIESTO, directorio_modelos)["ppocrv4-en"]

    assert ocr.sha256 == "3d37f552dfc2354511c40cd5b68793129ce54222aa2279bf4a54dcdee9cf134f"
    assert set(ocr.componentes) == {"ppocrv4_det.onnx", "ppocrv4_rec_en.onnx", "ppocrv4_en_dict.txt"}
