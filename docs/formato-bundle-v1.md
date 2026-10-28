# Bundle verificable `forensic-video/bundle/v1`

Un bundle acompaña a cada informe (pericial o investigativo). Sirve para que **un tercero, sin acceso al servidor ni al código del sistema**, compruebe por su cuenta que:

1. la cadena de custodia que se presenta está entera y encadenada, y cada asiento visible dice lo que dice;
2. el laboratorio la selló con su clave, y una cadena reconstruida no puede reproducir esos sellos;
3. cada archivo de evidencia es exactamente el que la cadena registró;
4. un bundle **pericial** no contiene nada de la vía «sólo investigación».

Este documento es la especificación completa: con él se puede escribir un verificador en cualquier lenguaje. El verificador de referencia (`tools/verificador`, en Go) **no comparte código con el servidor**, a propósito: es una segunda implementación independiente del mismo formato. Cómo se usa está en [`verificador.md`](verificador.md).

## Contenedor

Un archivo ZIP (se admiten ZIP64, *stored* y *deflate*). Las rutas usan `/`, son relativas y no contienen `..`, `\`, `:` ni segmentos vacíos. No puede haber dos entradas con el mismo nombre. Las entradas de directorio se ignoran.

**Toda entrada del ZIP está declarada en el manifiesto**, salvo el manifiesto y su firma. Una entrada no declarada es una falla: así nada puede viajar en el bundle sin estar firmado.

| Entrada | Contenido |
|---|---|
| `manifiesto.json` | Qué es el bundle y el SHA-256 de todo lo demás. |
| `manifiesto.json.sig` | Firma Ed25519 de los bytes exactos de `manifiesto.json`, en base64 (una línea, sin `=` faltantes; se tolera un salto de línea final). |
| `cadena.json` | La cadena de custodia del caso hasta la secuencia cubierta. |
| `sellos.json` | Los sellos de la cadena hasta esa secuencia. |
| `informe.pdf` | El informe legible. No se verifica su contenido, sólo su hash. |
| `LEEME.txt` | Instrucciones para el destinatario. |
| `evidencias/…` | Los archivos de evidencia embebidos. |

## Convenciones

- **Hash:** SHA-256, en hexadecimal minúscula (64 caracteres).
- **Timestamps:** UTC con exactamente seis decimales, `yyyy-MM-ddTHH:mm:ss.ffffffZ` (por ejemplo, `2026-10-28T15:04:05.123456Z`). Es la precisión con la que el servidor los guarda y los hashea.
- **Ids:** UUID en minúscula con guiones (`8-4-4-4-12`).
- **JSON:** UTF-8 sin BOM. Un objeto con claves repetidas, aunque difieran sólo en mayúsculas, es inválido. Las claves desconocidas son inválidas.
- **Clave del laboratorio:** Ed25519 (RFC 8032). Su id es `ed25519:` seguido del SHA-256 de los 32 bytes de la clave pública. Con openssl: `openssl pkey -in clave.pem -pubout -outform DER | tail -c 32 | sha256sum`.

### Campos canónicos

El hash de un asiento y el mensaje que firma un sello se arman con la misma codificación: una secuencia de campos, cada uno como

```
<nombre>:<longitud en bytes UTF-8 del valor, en decimal>:<valor>\n
```

concatenados en el orden indicado, y el resultado en UTF-8. El prefijo de longitud impide que un valor con saltos de línea simule otro campo.

## `manifiesto.json`

```json
{
  "formato": "forensic-video/bundle/v1",
  "tipo": "pericial",
  "caso": { "id": "…", "numeroExpediente": "EXP-2026-001", "descripcion": "…" },
  "generadoEn": "2026-10-28T15:04:05.123456Z",
  "generadoPor": "ana",
  "clave": { "id": "ed25519:…", "publica": "<64 hex: los 32 bytes de la clave pública>" },
  "cadena": { "hasta": 14, "hashCabeza": "…" },
  "archivos": [
    { "ruta": "cadena.json", "sha256": "…", "tamanoBytes": 18234 },
    { "ruta": "sellos.json", "sha256": "…", "tamanoBytes": 1210 },
    { "ruta": "informe.pdf", "sha256": "…", "tamanoBytes": 80211 },
    { "ruta": "LEEME.txt", "sha256": "…", "tamanoBytes": 1500 }
  ],
  "evidencias": [
    { "id": "…", "sha256": "…", "tamanoBytes": 524288, "nombre": "foto.jpg",
      "asientoDeOrigen": 2, "embebida": true, "ruta": "evidencias/<sha256>" },
    { "id": "…", "sha256": "…", "tamanoBytes": 4800000000, "nombre": "dvr.mp4",
      "asientoDeOrigen": 3, "embebida": false, "ruta": null }
  ]
}
```

- `tipo` es `pericial` o `investigativo`. Son dos informes distintos, emitidos por caminos distintos. Un bundle nunca es de los dos tipos.
- `clave.publica` es **informativa**. El verificador sólo la usa si su SHA-256 coincide con la huella que le da el usuario; nunca confía en la clave que trae el propio bundle.
- `cadena.hasta` y `cadena.hashCabeza` son la secuencia y el hash del último asiento incluido.
- `archivos` lista todos los archivos embebidos que no son evidencia.
- `evidencias` lista cada evidencia que el informe presenta:
  - `asientoDeOrigen` es la secuencia del asiento que la registró: `EvidenciaIngestada` si es un original, `OperacionAplicada` (u `OperacionIaAplicada`, sólo en un bundle investigativo) si es un derivado.
  - Si `embebida` es `true`, el archivo viaja en `ruta`.
  - Si es `false`, el archivo superó el umbral de tamaño del servidor y viaja aparte. El verificador lo busca **por su hash**, no por su nombre, en la carpeta que se le indique. `nombre` es el nombre declarado en la ingesta: dato no confiable, nunca una ruta.

## `cadena.json`

```json
{
  "formato": "forensic-video/custodia/v1",
  "caso": "…",
  "asientos": [
    { "secuencia": 1, "id": "…", "tipo": "CasoAbierto", "evidencia": null, "actor": "ana",
      "timestamp": "2026-10-28T14:00:00.000000Z", "detalle": "{\"numeroExpediente\":\"EXP-2026-001\",…}",
      "hashAnterior": "0000…0000", "hash": "…" },
    { "secuencia": 5, "opaco": true, "hashAnterior": "…", "hash": "…" }
  ]
}
```

Los asientos van ordenados por secuencia, desde 1, sin huecos, hasta `cadena.hasta`.

Un asiento **completo** trae todos sus campos. `detalle` es un string: el JSON del evento **exactamente como fue hasheado**. Hay que hashear ese string tal cual, sin reformatearlo. Su hash es el SHA-256 de estos campos canónicos, en este orden:

| Campo | Valor |
|---|---|
| `formato` | `forensic-video/custodia/v1` |
| `id` | id del asiento |
| `caso` | id del caso |
| `secuencia` | en decimal, sin ceros a la izquierda |
| `tipo` | el nombre del tipo, por ejemplo `EvidenciaIngestada` |
| `evidencia` | id de la evidencia, o vacío si es `null` |
| `actor` | usuario que lo asentó |
| `timestamp` | timestamp del servidor |
| `anterior` | `hashAnterior` |
| `detalle` | `detalle` |

Un asiento **opaco** (`"opaco": true`) trae sólo `secuencia`, `hashAnterior` y `hash`. Su contenido se excluyó del informe. Su hash no se puede recalcular, pero queda atado a la cadena: el asiento siguiente lo encadena, y ése sí se recalcula (o está sellado). El primer asiento encadena al génesis: 64 ceros.

### Qué es completo y qué es opaco

En un bundle **investigativo** todos los asientos son completos.

En un bundle **pericial**, un asiento es completo sólo si se cumplen las dos condiciones:

1. su tipo está en esta lista blanca cerrada: `CasoAbierto`, `AnalistaAsignado`, `CasoCerrado`, `EvidenciaIngestada`, `EvidenciaAccedida`, `ControlCruzadoCompletado`, `OperacionAplicada`, `OperacionReproducida`, `InformePericialEmitido`;
2. si refiere a una evidencia, esa evidencia está en `evidencias` del manifiesto, es decir, es admisible en juicio.

Todo lo demás es opaco. En particular, los análisis de IA, las operaciones de IA, lo que toca a un derivado «sólo investigación» y cualquier tipo que se agregue en el futuro. El informe dice cuántos asientos se excluyeron.

## `sellos.json`

```json
{
  "formato": "forensic-video/sello/v1",
  "sellos": [
    { "caso": "…", "secuencia": 14, "hashCabeza": "…", "selladoEn": "2026-10-28T15:04:05.000000Z",
      "claveId": "ed25519:…", "firma": "<base64 de los 64 bytes>" }
  ]
}
```

Cada sello es la firma Ed25519, con la clave del laboratorio, de estos campos canónicos en este orden:

| Campo | Valor |
|---|---|
| `formato` | `forensic-video/sello/v1` |
| `caso` | id del caso |
| `secuencia` | secuencia del asiento sellado |
| `cabeza` | `hashCabeza` |
| `sellado` | `selladoEn` |
| `clave` | `claveId` |

El servidor sella la cadena periódicamente y guarda los sellos fuera de la base. Antes de emitir un informe sella la cabeza, así que **el último sello del bundle cubre `cadena.hasta`**.

## Reglas de verificación

Un bundle es **ÍNTEGRO** si se cumplen todas. Es **INCOMPLETO** si lo único que falta son evidencias que viajan aparte y no se encontraron. En cualquier otro caso, está **MANIPULADO**.

1. **ZIP:** se lee, las rutas son válidas y únicas, y toda entrada está declarada.
2. **Manifiesto:** `formato` es `forensic-video/bundle/v1` y el `tipo` es válido. Si el usuario exige un tipo, coincide.
3. **Clave:** el SHA-256 de `clave.publica` coincide con la huella que da el usuario, y `clave.id` es esa huella.
4. **Firma del manifiesto:** `manifiesto.json.sig` verifica sobre los bytes exactos de `manifiesto.json`.
5. **Archivos:** cada entrada de `archivos`, y cada evidencia embebida, existe en el ZIP con ese SHA-256 y ese tamaño. Las rutas de evidencia empiezan con `evidencias/`. Una evidencia externa encontrada tiene que coincidir en hash y tamaño.
6. **Cadena:**
   - `cadena.json` es de este caso y tiene las secuencias 1..`hasta` sin huecos.
   - Cada `hashAnterior` es el `hash` del asiento previo (génesis para el primero).
   - Cada asiento completo recalcula a su `hash`.
   - El `hash` del último es `hashCabeza`.
7. **Sellos:**
   - Hay al menos uno.
   - Cada uno es de este caso, con `claveId` igual a la huella, una firma que verifica y una secuencia que existe en la cadena, cuyo `hash` es su `hashCabeza`.
   - El último cubre `cadena.hasta`.
8. **Evidencias ancladas:** el `asientoDeOrigen` de cada evidencia es completo, refiere a esa evidencia y su `detalle` coincide con ella:
   - Un original sale de un `EvidenciaIngestada`. Su detalle trae el mismo `sha256` y `tamanoBytes`.
   - Un derivado sale de un `OperacionAplicada` (en un bundle investigativo, también de un `OperacionIaAplicada`, con el mismo formato de `entrada` y `salida`). Su detalle trae `salida.evidenciaId`, `salida.sha256` y `salida.tamanoBytes` iguales, y su `entrada` (`evidenciaId` y `sha256`) es otra evidencia del bundle con un `asientoDeOrigen` anterior. Así el linaje llega hasta un original.
9. **Sólo en un bundle pericial:**
   - Todo asiento completo es de un tipo de la lista blanca.
   - Todo asiento completo que refiere a una evidencia refiere a una de `evidencias`.
   - El `via` del detalle de origen de cada evidencia es `AdmisibleEnJuicio`.

## Vectores de prueba

Para validar una implementación propia. Los dos lados del repositorio los fijan en sus tests: `RegistroDeCustodiaTests` y `FirmanteEd25519Tests` en .NET (y ambos se contrastaron con openssl y con una implementación de referencia en Python escrita desde este documento), `vectores_test.go` en el verificador.

**Asiento:**
- id `11111111-1111-1111-1111-111111111111`
- caso `22222222-2222-2222-2222-222222222222`
- secuencia `1`, tipo `CasoAbierto`, evidencia `null`, actor `ana`
- timestamp `2026-10-28T12:00:00.123456Z`
- `hashAnterior`: génesis
- detalle `{"numeroExpediente":"EXP-1"}`

Su hash:

```
5949e6267194282c3eaaac6e9730ed1779ab2fd20d3b20d1bc02cbe04dec2b0a
```

**Sello:** caso `22222222-2222-2222-2222-222222222222`, secuencia `1`, `hashCabeza` = el hash del asiento anterior, `selladoEn` `2026-10-28T13:00:00.000000Z`. Lo firma la clave del RFC 8032 (sección 7.1, TEST 1: semilla `9d61b19d…7f60`, pública `d75a9801…511a`), cuyo `claveId` es `ed25519:21fe31dfa154a261626bf854046fd2271b7bed4b6abe45aa58877ef47f9721b9`. Como Ed25519 es determinista, su firma es siempre:

```
61908f5f1905f2742aa988653dc556bfe09e5ec62bb07311eef3b7646ac7b4256329bfae5c3069c0626f5e69260f132e1d53defb46774e3336831f06bc75580c
```
