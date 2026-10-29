# Verificador independiente de bundles

`verificador` comprueba **por su cuenta** un bundle `forensic-video/bundle/v1` (el que acompaña a cada informe). Está pensado para que lo corra un perito de contraparte, un juzgado o cualquier tercero:

- **Es un único ejecutable sin dependencias.** No hay que instalar nada, no necesita permisos de administrador y no usa la red, el servidor del laboratorio ni su base de datos.
- **Es open-source y es una implementación independiente.** Está escrito en Go, aparte del servidor (.NET), y **no comparte código con él**: implementa la especificación pública [`formato-bundle-v1.md`](formato-bundle-v1.md). Si el servidor tuviera un error al armar o verificar la cadena, el verificador no lo hereda.
- **Llega a su propia conclusión.** No confía en nada de lo que el bundle dice de sí mismo: recalcula cada hash, rehace el encadenado y verifica cada firma con la clave que **usted** le indica.

## 1. Obtener la huella de la clave del laboratorio

La huella identifica la clave con la que el laboratorio firma. Tiene esta forma:

```
ed25519:21fe31dfa154a261626bf854046fd2271b7bed4b6abe45aa58877ef47f9721b9
```

**Pídala por un medio independiente del bundle:** por escrito al laboratorio, en el expediente o en la publicación oficial del organismo. El informe PDF también la imprime, pero viene en el mismo paquete que se quiere verificar: no alcanza como fuente.

El verificador nunca confía en la clave que trae el propio bundle. Sólo la usa si coincide con la huella que usted le indica.

## 2. Obtener el ejecutable

Los binarios se publican en los releases `verificador-v*` del repositorio (https://github.com/YagoGomez83/forensic-video/releases). Elija el de su sistema, junto con `SHA256SUMS` y `SHA256SUMS.sigstore.json`:

| Sistema | Archivo |
|---|---|
| Windows (64 bits) | `verificador-windows-amd64.exe` |
| Linux x86-64 | `verificador-linux-amd64` |
| Linux ARM64 | `verificador-linux-arm64` |
| macOS (Apple Silicon) | `verificador-darwin-arm64` |

Antes de usarlo, compruebe que el ejecutable sea el publicado:

- **Windows (PowerShell):** `Get-FileHash .\verificador-windows-amd64.exe -Algorithm SHA256`
- **Linux / macOS:** `sha256sum verificador-linux-amd64`

El resultado tiene que coincidir con la línea correspondiente de `SHA256SUMS`.

`SHA256SUMS` viene firmado sin claves (Sigstore) por el workflow de release del repositorio, en `SHA256SUMS.sigstore.json`. Con [cosign](https://docs.sigstore.dev) se comprueba que lo produjo ese workflow para ese release (reemplace `verificador-v1.0.0` por el nombre del release que descargó):

```
cosign verify-blob --bundle SHA256SUMS.sigstore.json \
  --certificate-identity https://github.com/YagoGomez83/forensic-video/.github/workflows/release-verificador.yml@refs/tags/verificador-v1.0.0 \
  --certificate-oidc-issuer https://token.actions.githubusercontent.com SHA256SUMS
```

El workflow sólo publica un release si el commit del tag ya está en la rama principal, y antes corre los mismos tests que el CI.

Si prefiere no confiar en el binario, compílelo usted desde el código fuente (sección 6).

## 3. Verificar

```
verificador --clave <huella> [--externos <carpeta>] [--exigir pericial] <bundle.zip>
```

Ejemplo en Windows (`cmd` o PowerShell):

```
verificador-windows-amd64.exe --clave ed25519:21fe…21b9 --externos D:\entrega\aparte --exigir pericial informe-pericial.zip
```

| Opción | Para qué |
|---|---|
| `--clave` | **Obligatoria.** La huella del paso 1. |
| `--externos` | Carpeta con las evidencias que, por su tamaño, se entregaron aparte del bundle (por ejemplo, un export de DVR de varios GB). Se buscan **por su hash**, así que no importa cómo se llamen los archivos. |
| `--exigir pericial` | Falla si el bundle no es pericial. Úselo siempre al recibir un informe pericial: así un bundle investigativo nunca pasa como prueba. |
| `--json` | Imprime el resultado completo en JSON, para procesarlo o archivarlo. |
| `--version` | Imprime la versión. |

## 4. Leer el resultado

Cada comprobación sale en una línea marcada `[OK]`, `[FALLA]` o `[INCOMPLETO]`. Una falla dice dónde está el problema: el asiento de la cadena o el archivo. La última línea da el veredicto:

| Resultado | Código de salida | Significa |
|---|---|---|
| `ÍNTEGRO` | 0 | Todas las comprobaciones pasaron. |
| `MANIPULADO` | 1 | Al menos una comprobación falló: el bundle fue alterado, no lo firmó esa clave o su contenido no es coherente. |
| `INCOMPLETO` | 2 | No hay fallas, pero faltan evidencias que viajan aparte (pase `--externos` con la carpeta correcta). |
| — | 3 | Error de uso: opciones inválidas, o no existe el bundle o la carpeta. |

Un bundle investigativo se anuncia con `*** INVESTIGATIVO — SÓLO INVESTIGACIÓN, NO ES PRUEBA ***`.

### Qué comprueba

Las reglas completas están en la especificación. En resumen:

1. Que la clave del bundle sea la de su huella y que el manifiesto esté firmado con ella.
2. Que cada archivo (cadena, sellos, informe PDF y evidencias) tenga el hash y el tamaño que declara el manifiesto, y que no viaje nada no declarado.
3. Que la cadena de custodia esté entera, sin huecos y encadenada, y que cada asiento visible recalcule a su hash.
4. Que los sellos del laboratorio estén firmados por la clave y coincidan con la cadena. **Una cadena reconstruida entera, con todos sus hashes recalculados, no coincide con un sello anterior.** Así se detecta aunque la cadena verifique por sí misma.
5. Que cada evidencia esté registrada en la cadena con ese hash, y cada derivado por una operación cuya entrada también esté en el bundle, hasta llegar a un original.
6. En un bundle pericial, que nada provenga de la vía «sólo investigación» (inteligencia artificial): toda evidencia tiene que ser admisible en juicio, y ningún asiento visible puede ser de IA ni de una evidencia excluida.

### Qué no comprueba

- **El texto del PDF.** El verificador comprueba que el PDF es el que el laboratorio firmó, no lo que dice. Lo que el informe afirma se contrasta con la cadena, que sí está verificada.
- **El contenido de los asientos opacos.** En un informe pericial, los asientos de material «sólo investigación» van opacos: se ve que existen y que están encadenados, pero su contenido no forma parte de la prueba.
- **La reproducción de las operaciones.** El bundle trae cada operación con sus parámetros y su entorno, que alcanzan para reproducirla con el worker de procesamiento del proyecto (imagen Docker fijada). Esa reproducción es un paso aparte.
- **Que la clave no haya sido robada.** Eso depende de cómo custodie el laboratorio su clave privada.

## 5. Un bundle de ejemplo

`tools/verificador/testdata` trae un bundle pericial y otro investigativo de prueba, con su huella (`huella.txt`) y el archivo externo (`dvr.mp4`):

```
cd tools/verificador
go run . --clave "$(cat testdata/huella.txt)" --externos testdata testdata/pericial.zip
```

## 6. Compilarlo desde el código fuente

Requiere Go (ver la versión mínima en `tools/verificador/go.mod`). No descarga dependencias: usa sólo la biblioteca estándar.

```
cd tools/verificador
go test ./...
CGO_ENABLED=0 go build -trimpath -ldflags "-s -w" -o verificador .
```

Para comparar su compilación con un binario publicado, use la versión de Go que figura en las notas del release y las mismas opciones que el workflow (`.github/workflows/release-verificador.yml`), incluida `-X main.version=<versión>-<los 12 primeros caracteres del commit>` (lo que imprime `--version` del binario publicado, por ejemplo `v1.0.0-982046d06294`). Las compilaciones de Go con `-trimpath` son reproducibles: el binario tiene que salir idéntico byte a byte.
