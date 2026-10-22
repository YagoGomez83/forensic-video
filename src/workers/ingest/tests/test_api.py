import hashlib
import json
from pathlib import Path

import pytest
from fastapi.testclient import TestClient

from app import ffprobe
from app.main import app

SALIDA_FFPROBE = {
    "format": {"filename": "/tmp/ingesta-x/contenido", "format_name": "mov,mp4,m4a", "duration": "12.5"},
    "streams": [{"index": 0, "codec_type": "video", "codec_name": "h264", "width": 1920, "height": 1080}],
}


@pytest.fixture
def cliente(monkeypatch: pytest.MonkeyPatch) -> TestClient:
    llamadas: list[Path] = []

    def analizar_falso(binario: str, archivo: Path) -> ffprobe.Metadatos:
        llamadas.append(archivo)
        return ffprobe.interpretar(json.dumps(SALIDA_FFPROBE), "7.1.1")

    monkeypatch.setattr(ffprobe, "ruta_ffprobe", lambda: "/usr/bin/ffprobe")
    monkeypatch.setattr(ffprobe, "analizar", analizar_falso)
    c = TestClient(app)
    c.llamadas = llamadas  # type: ignore[attr-defined]
    return c


def test_analizar_devuelve_hash_y_metadatos(cliente: TestClient):
    contenido = b"\x00\x00\x00\x18ftypmp42" + b"\x01" * 4096

    r = cliente.post("/v1/analizar", content=contenido, headers={"Content-Type": "application/octet-stream"})

    assert r.status_code == 200
    cuerpo = r.json()
    assert cuerpo["sha256"] == hashlib.sha256(contenido).hexdigest()
    assert cuerpo["tamanoBytes"] == len(contenido)
    assert cuerpo["ffprobe"]["version"] == "7.1.1"
    assert cuerpo["ffprobe"]["streams"][0]["codec_name"] == "h264"
    # La ruta temporal del worker no se filtra como dato de la evidencia.
    assert "filename" not in cuerpo["ffprobe"]["formato"]


def test_el_temporal_se_borra_al_terminar(cliente: TestClient):
    cliente.post("/v1/analizar", content=b"datos")

    (archivo,) = cliente.llamadas  # type: ignore[attr-defined]
    assert not archivo.exists()
    assert not archivo.parent.exists()


def test_una_evidencia_vacia_se_rechaza(cliente: TestClient):
    assert cliente.post("/v1/analizar", content=b"").status_code == 422


def test_sin_ffprobe_el_worker_no_analiza(monkeypatch: pytest.MonkeyPatch):
    monkeypatch.setattr(ffprobe, "ruta_ffprobe", lambda: None)

    assert TestClient(app).post("/v1/analizar", content=b"datos").status_code == 503
