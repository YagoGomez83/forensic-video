# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Estado del repositorio

Fase 1: servidor de custodia .NET 8 (`src/server`) y primer worker Python de ingesta (`src/workers/ingest`). Fase 3: cliente de escritorio Avalonia (`client/`) contra el backend actual (el worker de procesamiento sigue en el servidor). Fase 1.5: módulo de identidad (usuarios, login JWT, auditoría de accesos). Fase 2: worker de procesamiento (`src/workers/procesamiento`, OpenCV determinista), operaciones con derivados PNG y reproducción desde el asiento. Fase 4: worker de IA en la workstation (`src/workers/ia`, ONNX Runtime): detección, super-resolución y OCR, todo «sólo investigación», con su pantalla en el cliente. Fase 5: sellado de la cadena, informe pericial e investigativo (PDF + bundle verificable `forensic-video/bundle/v1`), verificador independiente en Go (`tools/verificador`), auditoría de accesos encadenada, mTLS y cadena de suministro del CI (Trivy, gitleaks, cosign). Lo que quedó pendiente al cerrar la Fase 5 está al final de `docs/architecture.md` («Pendientes al cerrar la sesión…»).

La fuente de diseño es `docs/architecture.md`; los cambios de diseño se hacen ahí. Lo que sigue resume lo que condiciona el código.

## Comandos

```bash
# Servidor (.NET). Warnings = errores (Directory.Build.props).
dotnet build src/server/Forensic.sln
dotnet test src/server/Forensic.sln               # todo; los de integración requieren Docker en marcha
dotnet test tests/Forensic.Domain.Tests           # sólo dominio, sin Docker
dotnet test src/server/Forensic.sln --filter "FullyQualifiedName~RegistroDeCustodiaTests.Eliminar_un_asiento_intermedio_se_detecta"

# Migraciones (dotnet-ef global). Revisar el SQL generado: los triggers van a mano. Dos contextos, cada uno con las suyas.
dotnet ef migrations add <Nombre> --project src/server/Forensic.Infrastructure --context ContextoForense --output-dir Persistencia/Migraciones
dotnet ef migrations add <Nombre> --project src/server/Forensic.Infrastructure --context ContextoDeIdentidad --output-dir Identidad/Migraciones

# Worker de ingesta (Python 3.12, desde src/workers/ingest)
py -3.12 -m venv .venv && .venv/Scripts/python -m pip install -e ".[dev]"
.venv/Scripts/python -m pytest                    # un test: -k test_analizar_devuelve_hash_y_metadatos
.venv/Scripts/python -m ruff check . && .venv/Scripts/python -m ruff format --check .

# Worker de procesamiento: igual, desde src/workers/procesamiento. tests/test_png_determinista.py tiene
# hashes dorados del PNG: si fallan, cambió el entorno y ningún derivado previo reproduciría.

# Worker de IA: igual, desde src/workers/ia, pero antes de pytest baja y verifica los pesos (sólo desarrollo y CI)
.venv/Scripts/python scripts/descargar_modelos.py
docker compose --profile workstation up -d worker-ia   # 127.0.0.1:8100; el cliente lo toma de FORENSIC_WORKER_IA
src/workers/ia/scripts/verificar_imagen.sh <imagen> --gpus   # PoC con GPU NVIDIA (driver >= 580)

# Verificador independiente de bundles (Go, sólo biblioteca estándar; desde tools/verificador)
go test ./... && go vet ./...                      # los tests de integración del informe también lo compilan: hace falta Go
go test ./verificacion -run Ejemplo -actualizar   # regenera testdata/ (bundles de ejemplo y su huella)
go run . --clave "$(cat testdata/huella.txt)" --externos testdata --exigir pericial testdata/pericial.zip

# Gate de la Fase 5 con todo real; deja los dos informes y la salida del verificador en la carpeta
FORENSIC_GATE_ARTEFACTOS=<carpeta> dotnet test tests/Forensic.IntegrationTests --filter GateDeLaFase5Tests

# Con poca memoria (16 GB): la imagen de IA se construye sola antes de la tanda de workers reales, con el
# builder clásico para que Testcontainers reuse las capas
DOCKER_BUILDKIT=0 docker build -t forensic-worker-ia:tests src/workers/ia

# Claves y certificados: siempre FUERA del repo (*.pem y *.key están en .gitignore)
openssl genpkey -algorithm ed25519 -out <fuera del repo>/clave-sellado.pem
scripts/generar-certificados-mtls.sh <carpeta fuera del repo> <host del servidor> workstation-01 [...]
scripts/verificar-imagenes.sh ghcr.io/<owner>/<repo>/servidor@sha256:...   # firma cosign + SBOM, antes de desplegar

# Cliente de escritorio (solución propia; los tests corren headless, sin pantalla)
dotnet test client/Forensic.Cliente.sln
FORENSIC_SERVIDOR=http://localhost:5065/ dotnet run --project client/Forensic.Cliente   # en claro, sólo loopback
# Contra un servidor con mTLS: FORENSIC_SERVIDOR=https://..., FORENSIC_CA_SERVIDOR=<ca.crt> y
# FORENSIC_CERTIFICADO_CLIENTE=<huella del almacén personal> (o una ruta PEM más FORENSIC_CLAVE_CLIENTE)

# Todo en local con Docker
cp .env.example .env                              # POSTGRES_PASSWORD, JWT_CLAVE_DE_FIRMA, seed del admin, CLAVE_SELLADO_ARCHIVO y TLS_DIR
docker compose up -d --build                      # servidor https+mTLS :5065, worker :8000, PostgreSQL :5433 (sólo 127.0.0.1)

# O el servidor fuera de Docker, contra el PostgreSQL del compose
dotnet user-secrets set "ConnectionStrings:Forense" "Host=localhost;Port=5433;Database=forense;Username=forense;Password=<la de .env>" --project src/server/Forensic.Api
dotnet user-secrets set "Identidad:Jwt:ClaveDeFirma" "<32+ bytes>" --project src/server/Forensic.Api
dotnet user-secrets set "Identidad:AdminInicial:Usuario" "<admin>" --project src/server/Forensic.Api
dotnet user-secrets set "Identidad:AdminInicial:Contrasena" "<12+ caracteres>" --project src/server/Forensic.Api
dotnet user-secrets set "Custodia:Sellado:RutaClavePrivada" "<clave-sellado.pem fuera del repo>" --project src/server/Forensic.Api
dotnet user-secrets set "Custodia:Sellado:RaizDeSellos" "<carpeta de sellos>" --project src/server/Forensic.Api
dotnet run --project src/server/Forensic.Api      # http://localhost:5065 en Development (sin TLS), migra al iniciar
```

Las versiones NuGet están centralizadas en `Directory.Packages.props` (los `.csproj` no llevan `Version`); EF Core va alineado a la versión que exige Npgsql (8.0.11). El SDK está fijado en `global.json`. Las dependencias Python van con versión exacta en `pyproject.toml`. **PDFsharp-MigraDoc queda en 6.2.4, la última MIT** (desde la 6.5.0 es AGPL-3.0): no subirlo sin revisar la licencia; `System.Security.Cryptography.Pkcs` va fijado en 8.0.2 por una vulnerabilidad de la 8.0.1 que trae PDFsharp. Las imágenes base van por digest en los Dockerfiles. CI (`.github/workflows/ci.yml`): build y test de todo (el job del servidor instala Go), verificador con sus binarios, Trivy de cada imagen y del repo (falla dura ante altas o críticas con arreglo), gitleaks sobre todo el historial (`.gitleaks.toml`), CodeQL `security-extended`, todas las acciones por SHA (Dependabot las actualiza) y, en main, imágenes publicadas en GHCR y firmadas con cosign (`.github/actions/publicar-y-firmar`).

## Estructura

- `src/server/Forensic.Domain`: agregados e invariantes. Sin paquetes externos; sólo BCL (SHA-256 incluido).
- `src/server/Forensic.Application`: casos de uso (`ServicioDeCasos`, `ServicioDeEvidencias`, `ServicioDeCustodia`, `ServicioDeOperaciones`, `Ia/ServicioDeAnalisisIa`, `Ia/ServicioDeOperacionesIa`) y puertos (`IPersistenciaForense`, `IAlmacenDeEvidencia`, `IWorkerDeIngesta`, `IWorkerDeProcesamiento`, `IActorActual`). El reloj es `TimeProvider`: nunca usar `DateTime.Now` ni un timestamp que venga del cliente.
- `src/server/Forensic.Infrastructure`: EF Core/Npgsql (`ContextoForense`, migraciones en `Persistencia/Migraciones`), clientes HTTP de los workers (`Workers/WorkerDeIngestaHttp`, `WorkerIngesta:UrlBase`; `Workers/WorkerDeProcesamientoHttp`, `WorkerProcesamiento:UrlBase`) y almacén de blobs en disco direccionado por hash (`raiz/ab/cd/<sha256>`, solo lectura).
- `src/server/Forensic.Application/Informes` y `Custodia/Sellado.cs`: `ServicioDeInformePericial`, `ServicioDeInformeInvestigativo`, `EmisionDeInforme` (mecánica común), `BundleV1` (el escritor del bundle), `ConsultaDeInformes`; `ServicioDeSellado`, `VerificadorDeCadena` y los puertos `IFirmanteDeCadena`, `IAlmacenDeSellos`, `IRedactorDeInformes`. En `Infrastructure`: `Sellado/` (`FirmanteEd25519`, `AlmacenDeSellosEnDisco`, `SelladorPeriodico`) e `Informes/` (`RedactorDePdf` con MigraDoc y las fuentes Liberation embebidas).
- `tools/verificador`: verificador independiente de bundles en Go, sin dependencias y sin código compartido con el servidor (`docs/formato-bundle-v1.md`, `docs/verificador.md`). `scripts/`: certificados de mTLS y verificación de imágenes firmadas.
- `src/server/Forensic.Api`: controllers (`Controllers/`) y `Seguridad/Mtls.cs`. Las excepciones se mapean a ProblemDetails en `ManejadorDeErrores` (401, 403 acceso, 404, 409 conflicto de cadena, 422 regla de dominio o contenido rechazado por el worker, 423 integridad o cadena comprometida, 502 divergencia del control cruzado, 503 worker caído).
- `tests/Forensic.Domain.Tests` y `tests/Forensic.IntegrationTests` (`WebApplicationFactory` + Testcontainers `postgres:17`, entorno `Testing`). `ContratoDelWorkerDeIngestaTests` construye la imagen real del worker desde su Dockerfile y prueba el contrato JSON de `/v1/analizar` contra `WorkerDeIngestaHttp`; `ContratoDelWorkerDeProcesamientoTests` y `ReproduccionConOpenCvTests` hacen lo mismo con el worker de procesamiento (el gate de la Fase 2 corre contra OpenCV real), y `CarrilDeIaDePuntaAPuntaTests` con el worker de IA (`WorkerDeIaReal`, en CPU). Las imágenes de los tres workers se construyen con `ImagenesDeWorkers` (nombre fijo `forensic-worker-<x>:tests`, una vez por proceso) y se reusan entre corridas: el caché de capas de Docker se invalida por contenido. La primera vez la de IA baja varios GB (~25 min); después la suite completa tarda ~1 min. Los cambios dejan imágenes `<none>` viejas: `docker image prune`. Todos están en `Forensic.sln` aunque vivan fuera de `src/server`.
- `client/`: cliente de escritorio, **una ventana al servidor, sin lógica forense propia**. `Forensic.Cliente.Api` (cliente REST tipado y sus propios contratos; no referencia al servidor), `Forensic.Cliente.Nucleo` (ViewModels MVVM con CommunityToolkit, `TransformacionDeVista`, formularios de operación, `Ia/` con la pantalla de IA y `OrquestadorDeAnalisisIa`), `Forensic.Cliente` (Avalonia 11, vistas XAML tontas, `LocalizadorDeVistas` por convención) y `Forensic.Cliente.Tests` (ViewModels con `ServidorFalso` + Avalonia.Headless con Skia real). `ContratoDelClienteDeEscritorioTests` y `ContratoDeLosFormulariosDelClienteTests` cruzan el cliente con el servidor real.
- `src/workers/ia`: FastAPI + ONNX Runtime, **en la workstation**: lo llama sólo el cliente local, nunca el servidor. `/v1/detectar` (YOLOX-s), `/v1/superresolucion` (Real-ESRGAN x4plus, PNG + cabecera `X-Reporte`) y `/v1/ocr` (PP-OCRv4, modelo compuesto). Pesos en `app/modelos.json` con SHA-256, verificados al construir la imagen y al arrancar; `restricciones.txt` fija todo (CUDA incluido). `exportacion/` regenera los ONNX publicados en el release `modelos-ia-v1`.
- `src/workers/ingest`: FastAPI. `POST /v1/analizar` recibe el archivo en crudo y devuelve SHA-256 + metadatos de ffprobe. Su hash es un control cruzado sobre **los mismos bytes crudos** que hashea el servidor: si el worker llegara a normalizar, reporta el hash del crudo de entrada, nunca el de su salida. La fuente de verdad es el hash que calcula el servidor al ingestar. `ServicioDeEvidencias` guarda el crudo, lo relee verificado del almacén y se lo pasa al worker; recién después crea la evidencia y el asiento, cuyo detalle guarda el hash del worker y los metadatos. Una divergencia (el worker reporta otro hash sobre bytes ya verificados) es un evento de seguridad: `DivergenciaDeControlCruzadoException`, `LogCritical`, 502 y no hay ingesta. Si el worker no responde, la ingesta procede con `controlCruzado.estado = "pendiente"` y se completa después con `POST .../evidencias/{id}/control-cruzado`, que agrega un asiento `ControlCruzadoCompletado`; lo puede completar el analista asignado con el caso abierto o un supervisor aunque el caso esté cerrado, y los pendientes no bloquean el cierre (`crucesPendientes` en el asiento `CasoCerrado` y en el export; `controlCruzadoPendiente` por evidencia en el listado) (decisión 5 de "Decisiones cerradas" en `docs/architecture.md`). Si ffprobe no reconoce el formato, la evidencia se custodia igual con `metadatos: null` y `motivoSinMetadatos`. ffprobe se invoca con una lista de argumentos sobre una ruta temporal propia, nunca con el nombre de la evidencia.

Detalles que no se ven a simple vista:
- **Cliente, reglas que no se negocian:** no calcula hashes, no decide admisibilidad ni permisos (no oculta botones por rol: muestra el 403 del servidor), no toca la base ni al worker. Todo pasa por `ClienteForense` con el JWT, que vive **sólo en memoria** (`SesionEnMemoria`); un 401 de una sesión activa la expira y `VentanaViewModel` vuelve al login. Los ViewModels absorben `SesionExpiradaException` (si escapa de un `AsyncRelayCommand`, cierra la app). Sin previews: se aplica de verdad y se muestra el derivado del servidor. La evidencia se lee a memoria para verla y se descarga en streaming sólo al destino que elige el analista; nada a temporales.
- **Cambio forzado de contraseña en el cliente (opción (b)):** el login no lo informa; `ClienteForense.RequiereCambioDeContrasenaAsync` pide `GET /casos` y un 403 significa contraseña temporal. Si el login empieza a devolverlo, sólo cambia ese método.
- **Visor:** `TransformacionDeVista` traduce pantalla↔imagen (el píxel i ocupa [i, i+1)); lo que viaja al servidor son siempre píxeles enteros de la imagen original. Avalonia (Skia) no aplica la orientación EXIF, igual que el worker: hay un test que lo fija. Las unidades de punto fijo se convierten con `PuntoFijo` (decimal exacto; si sobran decimales avisa, nunca redondea). Sumar una operación al cliente: un `FormularioDeX` en `Nucleo/Operaciones`, su `FormularioDeXVista`, el registro en `VisorViewModel.Formularios`, tests en `FormulariosTests` y una fila en `ContratoDeLosFormulariosDelClienteTests`.
- **Avalonia 11, no 12:** la 12 trae analizadores que exigen Roslyn 4.14, más nuevo que el del SDK fijado en `global.json`.
- **Identidad ≠ custodia:** login, logins fallidos, altas, cambios de rol y de contraseña van a `identidad."AuditoriaDeAccesos"` (append-only con triggers y encadenada por hash: un trigger `BEFORE INSERT` con advisory lock la encadena, formato `forensic-video/accesos/v1`, y `CadenaDeAccesos` la verifica en `GET /auditoria-de-accesos/verificacion`), nunca a `AsientosDeCustodia`. `ContextoDeIdentidad` tiene su schema, sus migraciones (`Infrastructure/Identidad/Migraciones`) y no comparte transacción con la custodia.
- **Autenticación:** `POST /auth/login` emite un JWT HS256 de 15 min (sin refresh). `[Authorize]` por defecto (`ValidacionDeSesion`): en cada petición el token se contrasta con la base, así que deshabilitar o cambiar un rol invalida los tokens al instante; con contraseña inicial o reseteada sólo se puede usar `POST /auth/contrasena`. `ActorDelToken` toma el actor del principal. Login rechazado = un único 401 genérico (siempre se verifica un hash, exista o no el usuario); lockout de 15 min tras 5 fallos, y rate limiting por IP en `/auth/login` y `/auth/contrasena` (`LimiteDeLogin`, 429), que es la defensa primaria porque el lockout no es seguro ante concurrencia. Limitaciones conocidas en `docs/architecture.md` (la auditoría de accesos no tiene hash-chain). Argon2id vía libsodium (Sodium.Core), nunca criptografía propia.
- **Arranque:** `Program.cs` no arranca sin `Identidad:Jwt:ClaveDeFirma` (≥ 32 bytes), sin la clave de sellado (`Custodia:Sellado:RutaClavePrivada`, Ed25519 en PKCS#8 PEM), en Production sin mTLS (`Tls:*`, salvo `Tls:SinTls=true`) ni, si no hay ningún admin activo, sin `Identidad:AdminInicial:Usuario`/`Contrasena`. Todo desde user-secrets o variables de entorno; nada de credenciales por defecto.
- **Rol Admin:** administra usuarios (`/usuarios`) y no accede a casos (separación de funciones). Nadie se administra a sí mismo, así que siempre queda un admin activo.
- **Tests:** `AutenticacionPorCabeceras` (sólo en el proyecto de tests, registrado por `ServidorForense`) toma el actor de `X-Usuario`/`X-Rol`; sin esas cabeceras la petición va por JWT. `IpDePrueba` simula la IP remota con `X-Ip-De-Prueba` y el límite de login se sube en `ServidorForense` (los tests de límite lo bajan con `WithWebHostBuilder`). El servidor real no tiene autenticación por cabeceras. `IdentidadTests` cubre el camino JWT.
- **Asentar en la cadena:** leer `ObtenerUltimoAsientoAsync` → `RegistroDeCustodia.Asentar(...)` → `Agregar` la entidad y el asiento → un solo `GuardarCambiosAsync`. El índice único `(CasoId, Secuencia)` serializa las escrituras concurrentes: el perdedor recibe `ConflictoDeCustodiaException` (409).
- **Formato canónico del hash** (`AsientoDeCustodia.CalcularHash`, `forensic-video/custodia/v1`): campos con prefijo de longitud y timestamp UTC truncado a microsegundos (la precisión de PostgreSQL). Cambiarlo invalida todas las cadenas existentes: hay que versionarlo.
- **Triggers append-only:** están en la migración `Inicial` (SQL a mano) y bloquean UPDATE/DELETE/TRUNCATE en `AsientosDeCustodia` y `Evidencias`.
- **Operaciones (Fase 2):** `CatalogoDeOperaciones` (dominio) y `CATALOGO` (worker) son espejo y sólo tienen operaciones deterministas de OpenCV: recorte, perspectiva, rotación (exacta y por ángulo), denoise (NL-means, mediana), realce (unsharp, CLAHE), tonal (brillo-contraste, gamma, ecualización) y extracción de frame. Tipos versionados (`opencv.recorte/v1`); parámetros sólo enteros o strings de lista cerrada (las magnitudes continuas en punto fijo: centésimas, milésimas), en JSON canónico que se asienta y se reenvía **sin re-serializar**. Sumar una operación: clase en `Domain/Operaciones` + entrada en los dos catálogos + filas en `CatalogoDeOperacionesTests`, tests de píxeles en `test_operaciones.py` y una fila en `ReproduccionConOpenCvTests`. Cambiar el comportamiento de una existente exige versión nueva (`/v2`).
- **IA (Fase 4), sólo investigación** (decisión 9 de `docs/architecture.md`): `CatalogoDeIa` tiene los tipos (`ia.deteccion/v1`, `ia.ocr/v1`, `ia.superresolucion/v1`) y la lista blanca de modelos por capacidad (id, versión, SHA-256); está separado de `CatalogoDeOperaciones` en los dos sentidos. Un análisis (detección, OCR) es un `AnalisisIa` con `Hallazgo`s y un asiento `HallazgosRegistrados` por ejecución, también con 0 hallazgos; cada análisis produce su tipo de hallazgo (clase o texto leído). La super-resolución es `OperacionForense.RegistrarIa` + derivado + asiento `OperacionIaAplicada`, y reproducirla da 422. El servidor verifica lo que puede (hash de entrada = evidencia custodiada, lista blanca, PNG re-hasheado); el entorno queda como procedencia `declarada-por-workstation`. El reporte del worker se reenvía y se asienta **sin re-serializar**. CHECK y triggers en la base: lo de IA no puede tener otra vía.
- **Cliente e IA:** `ClienteWorkerIa` sólo acepta loopback (`FORENSIC_WORKER_IA`, por defecto `127.0.0.1:8100`); `OrquestadorDeAnalisisIa` no reporta nada si el worker falla. La IA vive en su pantalla (`AnalisisIaViewModel`, desde el visor), nunca en el combo de operaciones: franja «SÓLO INVESTIGACIÓN», hallazgos dibujados por `LienzoDeEvidencia.Hallazgos` y descritos como lo que dijo el modelo («el modelo leyó «ABC123» con 0,82»). El cliente corre con globalización invariante: nada de `CultureInfo` por nombre.
- **Asiento `OperacionAplicada`** (`forensic-video/operacion/v1`): tipo, parámetros y entorno como JSON crudo, entrada y salida con hashes, hash de píxeles y vía. `OperacionAsentada.Desde` lo lee; basta para reproducir. `POST .../reproducir` re-ejecuta desde ese asiento (sólo con la cadena íntegra y coincidente con sus sellos) y asienta `OperacionReproducida`; `GET .../linaje` da el historial. Operación, derivado y asiento van en una sola transacción; `OperacionesForenses` es inmutable con triggers.
- **Sellado (Fase 5):** `SelloDeCadena` (`forensic-video/sello/v1`) firma con la clave del laboratorio el hash de un eslabón; `RegistroDeCustodia.VerificarSellos` delata una cadena reconstruida entera o truncada aunque verifique por sí misma. El sellado periódico (`Custodia:Sellado:Intervalo`, 1 h; 0 lo apaga en los tests) nunca sella una cadena rota. Para algo pericial (reproducir, emitir un informe) se usa `VerificadorDeCadena.ObtenerConfiableAsync`: íntegra y coincidente con sus sellos, o 423. El export de custodia trae `sellado`.
- **Informes (Fase 5):** el pericial y el investigativo son caminos separados (servicio, endpoint, asiento y bundle propios; cada tipo sólo se descarga por su ruta). Lo que entra al pericial lo decide **sólo** `ContenidoPericial`: una evidencia pasa por `ExigirAdmisibleEnJuicio()` y un asiento va completo sólo si su tipo está en `ContenidoPericial.TiposCompletos` y su evidencia entró; si no, opaco. Un tipo de asiento nuevo es opaco por defecto; sumarlo a la lista blanca exige cambiar también `docs/formato-bundle-v1.md` y `tiposPericiales` en el verificador (los tests de los dos lados la contrastan con el documento). Cambiar el formato del bundle exige versionarlo y actualizar los vectores de los dos lados.
- **mTLS (Fase 5):** el servidor valida el certificado de cliente contra la CA del laboratorio (EKU de cliente, sin revocación) y el cliente (`CanalSeguro`) el del servidor contra la misma CA, con el nombre de la URL. En Windows, SslStream no usa claves efímeras: un certificado cargado de PEM se re-importa desde PKCS#12. TestServer no hace TLS: los tests de mTLS levantan Kestrel real (`MtlsTests`).

## Qué es

Sistema open-source, on-premise y multi-analista de análisis forense de video/imagen que reemplaza progresivamente a Amped FIVE. **La cadena de custodia es la arquitectura, no un módulo**: filtros e IA son plugins alrededor de un registro inmutable y reproducible de qué se le hizo a cada evidencia. Nada de ingeniería inversa ni binarios de Amped u otro software propietario.

## Arquitectura

Híbrida: el cómputo corre en la workstation del analista (GPU NVIDIA local); la custodia vive en un servidor central sin GPU que no procesa imágenes, sólo guarda, versiona y audita.

- **Cliente (workstation):** UI Avalonia (C#, decidido; WPF sólo si un control de terceros lo exige) → orquestador local → worker de IA Python + FastAPI con ONNX Runtime (YOLOX-s, Real-ESRGAN x4plus, PP-OCRv4; no PyTorch: los `.pt` son pickles). Los workers nunca se exponen fuera de la workstation.
- **Servidor de custodia:** .NET 8 + ASP.NET Core, monolito modular con bounded contexts (no microservicios: fragmentaría las transacciones de la cadena de custodia). API REST con authN/Z por rol y por caso, núcleo de dominio, servicio de auditoría (hash-chain). Cliente↔servidor por HTTPS/mTLS.
- **Persistencia:** tres cosas separadas que no se mezclan — PostgreSQL vía EF Core (casos/metadatos), almacén de blobs de evidencia (originales inmutables + SHA-256), y el log de custodia append-only encadenado.
- **Cola:** empezar con cola en PostgreSQL; RabbitMQ sólo si hace falta.
- **Ingesta:** sólo se hashean los bytes crudos y se leen metadatos (ffprobe, sólo lectura); **nunca se transcodifica en la ingesta**. La normalización de contenedores/códecs y la extracción de frames (celulares; Milestone MKV/AVI/MP4; HikVision) son Operaciones de la Fase 2 que producen derivados, no pasos de ingesta.
- **Identidad:** no hay Active Directory. Módulo de identidad propio en el servidor (roles analista/supervisor/auditor/admin, Argon2id, JWT de vida corta), con auditoría de accesos separada del log de custodia: un evento de identidad nunca es un asiento de custodia.

Clean Architecture + DDD: la capa de dominio no referencia OpenCV, PyTorch, EF Core ni ASP.NET. Si una regla de custodia importa algo de infraestructura, es un error de diseño.

## Modelo de dominio

Agregados: `Caso`, `Evidencia`, `OperacionForense`, `RegistroDeCustodia`.

Invariantes no negociables:
1. **El hash es de los bytes crudos exactos**, calculado sobre el archivo tal como se recibe, antes de validar, inspeccionar o normalizar nada. Esos bytes exactos son el blob original que se preserva. Sin hash no hay ingesta. Se verifica el hash en cada lectura; discrepancia = alerta y bloqueo.
2. Ninguna operación modifica el original: siempre produce un derivado nuevo con su propio hash, que referencia a su padre. **Normalizar o transcodificar (FFmpeg) es una Operación, no parte de la ingesta**: genera un derivado con su propio asiento, clasificado con criterio estricto (un transcode altera píxeles → no es automáticamente «admisible en juicio»).
3. Cada `OperacionForense` (tipo, parámetros, hash de entrada y salida, actor) se registra atómicamente con su asiento en el log. Si falla el asiento, la operación no ocurrió. Es inmutable una vez registrada.
4. `RegistroDeCustodia` es append-only y encadenado: cada registro incluye el hash del anterior.
5. Actor autenticado y **timestamp del servidor**, nunca del cliente. Nada pericial se decide confiando en el cliente.
6. Una evidencia pertenece a un único caso; el acceso se autoriza a nivel de caso, verificado en el servidor en cada operación.
7. **Doble vía:** todo derivado es «admisible en juicio» (sólo operaciones deterministas, p. ej. OpenCV) o «sólo investigación» (tocado por IA). La marca se propaga a todos los descendientes y el sistema debe hacer imposible presentar un derivado «sólo investigación» como prueba.

Log de custodia (prueba pericial, inmutable) y log técnico (Serilog/OpenTelemetry, operacional y rotativo) son cosas distintas: nunca mezclarlos.

**Lenguaje ubicuo:** usar los términos forenses en español (Evidencia, Derivado, Cadena de Custodia, Operación, Reproducción), no genéricos como «archivo» o «job».

## Reproducibilidad

Desde el log debe poder regenerarse cualquier salida a partir del original. Fijar versión de modelo, pesos, seeds y entorno (imagen Docker); documentar explícitamente el no-determinismo residual de modelos de deep learning.

Reglas del worker de procesamiento (no negociables): `opencv-python-headless` y `numpy` con versión exacta; `entorno.configurar()` (un hilo, sin OpenCL, `useOptimized=False`) **en el mismo hilo que procesa**, porque OpenCL es por hilo en OpenCV; derivados siempre PNG con `codificar_png` (compresión y estrategia fijas, sólo IHDR/IDAT/IEND); decodificar con `IMREAD_UNCHANGED | IMREAD_IGNORE_ORIENTATION` (nunca aplicar la rotación EXIF) y rechazar multipágina; el entorno real se reporta en cada respuesta y va al asiento. La reproducción está garantizada **dentro de la misma imagen Docker**: entre plataformas difieren al menos la decodificación con pérdida (JPEG/MJPEG) y `math.pow` de la LUT de gamma (ver `docs/architecture.md`).

Lo de IA no se reproduce: no se verificó igualdad bit a bit entre CPU y CUDA ni entre GPUs. Se garantiza la procedencia (modelo y hash de sus pesos, entrada exacta, proveedor efectivo y entorno declarado, actor y timestamp del servidor). Cambiar un modelo exige versión nueva en `modelos.json` y en la lista blanca.

## Seguridad

La evidencia es entrada no confiable: validar tipo, tamaño y metadatos en la API. Consultas parametrizadas vía EF Core; nombres de archivo nunca interpolados en rutas ni comandos (relevante al invocar FFmpeg). Sin secretos en código ni imágenes Docker.

## Roadmap (el orden no es negociable)

1. **Cimiento de custodia** — servidor .NET + PostgreSQL, sin UI: ingesta + SHA-256 + log encadenado + gestión de casos. Gate: una evidencia se ingesta, hashea y audita de punta a punta (PoC: ingesta, hash, un registro encadenado, export del registro).
2. **Mejora clásica** — workers Python/OpenCV (deblur, denoise, geometría), cada operación asentada en el log. Gate: operación reproducible al 100%.
3. **Cliente de escritorio + GPU** — Avalonia, orquestador local.
4. **Analítica e IA** — super-resolución, YOLO, OCR, tracking; modelos versionados en Docker.
5. **Informe pericial + endurecimiento** — export de la cadena, CI/CD con SAST, escaneo de dependencias/imagen, firma de imágenes. Gate técnico cerrado; pendientes al final de `docs/architecture.md`.

No construir filtros antes que la custodia.
