# Datos de los tests del worker de IA

## `hyde-st-sf.jpg`

Foto real para `tests/test_deteccion_real.py`: una calle con personas y autos, para comprobar que
YOLOX-s con los pesos reales encuentra lo que hay.

| | |
|---|---|
| Título | Looking S on Hyde St, between Beach and Jefferson, SF (en 500px: «Ain T Nobody Got Time For Crosswalks») |
| Autor | Mohamed Hayibor, 26 de febrero de 2016 |
| Licencia | [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) (dominio público): se usa sin atribución obligatoria; se la da igual |
| Commons | https://commons.wikimedia.org/wiki/File:Looking_S_on_Hyde_St,_between_Beach_and_Jefferson,_SF.jpg |
| Origen | https://500px.com/photo/149468783/ain-t-nobody-got-time-for-crosswalks-by-mohamed-hayibor, publicada con CC0 e importada a Commons en 2018 (copia en https://web.archive.org/web/201807id_/https://drscdn.500px.org/photo/149468783/m%3D2048/v2) |
| Archivo | el original de Commons, sin tocar: 2048×1366, 999 628 bytes |
| SHA-1 | `7cf5d5d7105ac855c5b28b0e8133bce98d25c493` (el que publica Commons) |
| SHA-256 | `24f4c03c6046fe388499c8768e9a3acaf75f58f7757786fd7cbfd70483443089` (lo verifica el test) |

Muestra personas reconocibles en la vía pública. No se usa fuera de los tests.

## `chatham-the-brook.jpg`

Foto real para `tests/test_ocr_real.py`: un cruce peatonal con el cartel «Crown House» en la pared, para
comprobar que PP-OCRv4 con los pesos reales lee texto en una escena. El texto pintado en el piso
(«LOOK LEFT») no se lee a la escala del modelo y el test no lo pide.

| | |
|---|---|
| Título | The Brook pedestrian crossing - Chatham |
| Autor | OathOn (usuario de Wikimedia Commons), obra propia, 1 de julio de 2025 |
| Licencia | [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) (dominio público): se usa sin atribución obligatoria; se la da igual |
| Commons | https://commons.wikimedia.org/wiki/File:The_Brook_pedestrian_crossing_-_Chatham.jpg |
| Archivo | el original de Commons, sin tocar (una sola versión): 3296×2367, 2 678 380 bytes |
| SHA-1 | `6116e6badb60a7514b383e7413921e87fa640f7f` (el que publica Commons) |
| SHA-256 | `ed9d5e4960a944b3c49116de6cd4ee8cbb054cf1db16cd7da1bcbb9e30160144` (lo verifica el test) |
