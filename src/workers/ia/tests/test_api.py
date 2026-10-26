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


# --- POST /v1/detectar ---

YOLOX_SHA256 = "c5c2d13e59ae883e6af3b45daea64af4833a4951c92d116ec270d9ddbe998063"


def _png(pixeles) -> bytes:
    import cv2

    return cv2.imencode(".png", pixeles)[1].tobytes()


def _detectar(cliente, contenido: bytes, parametros: str = '{"umbralConfianzaMilesimas":500}'):
    return cliente.post(
        "/v1/detectar",
        data={"parametros": parametros},
        files={"entrada": ("entrada.bin", contenido, "application/octet-stream")},
    )


@pytest.fixture(scope="module")
def cliente(directorio_modelos):
    with TestClient(crear_app(directorio_modelos)) as c:
        yield c


def test_detectar_devuelve_la_procedencia_completa(cliente):
    import hashlib

    import numpy as np

    contenido = _png(np.full((240, 320, 3), 128, dtype=np.uint8))

    r = _detectar(cliente, contenido, '{ "umbralConfianzaMilesimas": 500 }')

    assert r.status_code == 200
    cuerpo = r.json()
    assert cuerpo["analisis"] == "ia.deteccion/v1"
    assert cuerpo["parametros"] == {"umbralConfianzaMilesimas": 500}
    assert cuerpo["parametrosCanonicos"] == '{"umbralConfianzaMilesimas":500}'
    assert cuerpo["modelo"] == {"id": "yolox-s", "version": "0.1.1rc0", "sha256": YOLOX_SHA256}
    assert cuerpo["entrada"] == {
        "sha256": hashlib.sha256(contenido).hexdigest(),
        "ancho": 320,
        "alto": 240,
    }
    entorno = cuerpo["entorno"]
    assert entorno["proveedor"] in ("CUDAExecutionProvider", "CPUExecutionProvider")
    assert entorno["onnxruntime"] == "1.31.0"
    assert entorno["postproceso"] == {"lado": 640, "relleno": 114, "nmsIou": "0.45"}
    # Una imagen gris lisa: el modelo no encuentra nada, y eso también es un resultado.
    assert cuerpo["hallazgos"] == []


def test_detectar_en_cpu_da_lo_mismo_dos_veces(cliente):
    import numpy as np

    rng = np.random.default_rng(1234)
    contenido = _png(rng.integers(0, 256, size=(200, 300, 3), dtype=np.uint8))

    primera = _detectar(cliente, contenido, '{"umbralConfianzaMilesimas":10}').json()
    segunda = _detectar(cliente, contenido, '{"umbralConfianzaMilesimas":10}').json()

    if primera["entorno"]["proveedor"] == "CPUExecutionProvider":
        assert primera["hallazgos"] == segunda["hallazgos"]
    for h in primera["hallazgos"]:
        assert set(h) == {"region", "clase", "confianza", "crudo"}
        assert 0.01 <= h["confianza"] <= 1


def test_detectar_rechaza_lo_que_no_es_imagen(cliente):
    r = _detectar(cliente, b"no es una imagen")

    assert r.status_code == 422


def test_detectar_rechaza_parametros_invalidos(cliente):
    import numpy as np

    r = _detectar(cliente, _png(np.zeros((8, 8, 3), np.uint8)), '{"umbralConfianzaMilesimas":0}')

    assert r.status_code == 422


def test_detectar_rechaza_entradas_demasiado_grandes(cliente, monkeypatch):
    import app.main

    monkeypatch.setattr(app.main, "TAMANO_MAXIMO_BYTES", 10)

    r = _detectar(cliente, b"0123456789A")

    assert r.status_code == 413
