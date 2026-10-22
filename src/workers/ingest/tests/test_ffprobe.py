import json

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
