"""La imagen instala restricciones.txt: lo que declara pyproject.toml tiene que coincidir con eso."""

import re
import tomllib
from pathlib import Path

RAIZ = Path(__file__).resolve().parent.parent


def _normalizar(nombre: str) -> str:
    return re.sub(r"[-_.]+", "-", nombre).lower()


def _restricciones() -> dict[str, str]:
    versiones = {}
    for linea in (RAIZ / "restricciones.txt").read_text(encoding="utf-8").splitlines():
        linea = linea.strip()
        if linea and not linea.startswith("#"):
            nombre, version = linea.split("==")
            versiones[_normalizar(nombre)] = version
    return versiones


def _declaradas() -> list[str]:
    proyecto = tomllib.loads((RAIZ / "pyproject.toml").read_text(encoding="utf-8"))["project"]
    return [*proyecto["dependencies"], *proyecto["optional-dependencies"]["gpu"]]


def test_toda_restriccion_tiene_version_exacta():
    for linea in (RAIZ / "restricciones.txt").read_text(encoding="utf-8").splitlines():
        if linea.strip() and not linea.startswith("#"):
            assert re.fullmatch(r"[A-Za-z0-9_.-]+==[^=\s]+", linea.strip()), linea


def test_las_dependencias_directas_coinciden_con_las_restricciones():
    restricciones = _restricciones()
    for dependencia in _declaradas():
        nombre, version = re.fullmatch(r"([A-Za-z0-9_.-]+)(?:\[[^\]]+\])?==(\S+)", dependencia).groups()
        assert restricciones.get(_normalizar(nombre)) == version, dependencia


def test_las_bibliotecas_de_cuda_estan_clavadas():
    assert any(n.startswith("nvidia-cudnn") for n in _restricciones())
    assert any(n.startswith("nvidia-cublas") for n in _restricciones())
