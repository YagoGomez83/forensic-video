import json
import subprocess

import pytest

from app import ffprobe


def test_interpretar_descarta_la_ruta_temporal():
    salida = json.dumps({"format": {"filename": "/tmp/x", "format_name": "avi"}, "streams": []})

    m = ffprobe.interpretar(salida, "7.1.1")

    assert m.formato == {"format_name": "avi"}
    assert m.streams == []
    assert m.version_ffprobe == "7.1.1"


@pytest.mark.parametrize("salida", ["no es json", json.dumps({"streams": []})])
def test_interpretar_rechaza_salidas_sin_contenedor(salida: str):
    with pytest.raises(ffprobe.FfprobeError):
        ffprobe.interpretar(salida, "7.1.1")


def test_el_motivo_del_fallo_no_cita_la_ruta_temporal(monkeypatch: pytest.MonkeyPatch, tmp_path):
    archivo = tmp_path / "contenido"

    def run_falso(args, **kwargs):
        return subprocess.CompletedProcess(
            args, 1, "", f"{archivo}: Invalid data found when processing input\n"
        )

    monkeypatch.setattr(subprocess, "run", run_falso)

    with pytest.raises(ffprobe.FfprobeError) as ex:
        ffprobe.analizar("ffprobe", archivo)

    assert str(archivo) not in str(ex.value)
    assert "<evidencia>: Invalid data" in str(ex.value)
