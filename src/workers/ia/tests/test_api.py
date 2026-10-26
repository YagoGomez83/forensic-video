import json

import pytest
from fastapi.testclient import TestClient

from app.main import crear_app
from app.modelos import ModeloInvalido


def test_entorno_reporta_modelos_verificados_y_proveedor(directorio_modelos):
    with TestClient(crear_app(directorio_modelos)) as cliente:
        r = cliente.get("/v1/entorno")

    assert r.status_code == 200
    cuerpo = r.json()
    yolox = cuerpo["modelos"]["yolox-s"]
    assert yolox["sha256"] == "c5c2d13e59ae883e6af3b45daea64af4833a4951c92d116ec270d9ddbe998063"
    assert yolox["proveedor"] in ("CUDAExecutionProvider", "CPUExecutionProvider")
    assert cuerpo["entorno"]["onnxruntime"] == "1.31.0"
    assert "gpu" in cuerpo["entorno"]


def test_el_worker_no_arranca_con_un_peso_alterado(tmp_path, directorio_modelos):
    # Copia del directorio real con un byte cambiado en el peso.
    pesos = (directorio_modelos / "yolox_s.onnx").read_bytes()
    (tmp_path / "yolox_s.onnx").write_bytes(pesos[:-1] + bytes([pesos[-1] ^ 0xFF]))

    with pytest.raises(ModeloInvalido), TestClient(crear_app(tmp_path)):
        pass


def test_el_manifiesto_del_worker_no_tiene_entradas_sin_hash():
    from app.modelos import MANIFIESTO

    for entrada in json.loads(MANIFIESTO.read_text(encoding="utf-8"))["modelos"]:
        assert len(entrada["sha256"]) == 64
        int(entrada["sha256"], 16)
        assert entrada["licencia"]
