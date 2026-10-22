# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Estado del repositorio

Fase 1: servidor de custodia .NET 8 (`src/server`) y primer worker Python de ingesta (`src/workers/ingest`). `client/` (Avalonia) llega en la Fase 3. Todavía no existe el módulo de identidad.

La fuente de diseño es `docs/architecture.md`; los cambios de diseño se hacen ahí. Lo que sigue resume lo que condiciona el código.

## Comandos

```bash
# Servidor (.NET). Warnings = errores (Directory.Build.props).
dotnet build src/server/Forensic.sln
dotnet test src/server/Forensic.sln               # todo; los de integración requieren Docker en marcha
dotnet test tests/Forensic.Domain.Tests           # sólo dominio, sin Docker
dotnet test src/server/Forensic.sln --filter "FullyQualifiedName~RegistroDeCustodiaTests.Eliminar_un_asiento_intermedio_se_detecta"

# Migraciones (dotnet-ef global). Revisar el SQL generado: los triggers van a mano.
dotnet ef migrations add <Nombre> --project src/server/Forensic.Infrastructure --output-dir Persistencia/Migraciones

# Worker de ingesta (Python 3.12, desde src/workers/ingest)
py -3.12 -m venv .venv && .venv/Scripts/python -m pip install -e ".[dev]"
.venv/Scripts/python -m pytest                    # un test: -k test_analizar_devuelve_hash_y_metadatos
.venv/Scripts/python -m ruff check . && .venv/Scripts/python -m ruff format --check .

# Todo en local con Docker
cp .env.example .env                              # definir POSTGRES_PASSWORD
docker compose up -d --build                      # servidor :5065, worker :8000, PostgreSQL :5433 (sólo 127.0.0.1)

# O el servidor fuera de Docker, contra el PostgreSQL del compose
dotnet user-secrets set "ConnectionStrings:Forense" "Host=localhost;Port=5433;Database=forense;Username=forense;Password=<la de .env>" --project src/server/Forensic.Api
dotnet run --project src/server/Forensic.Api      # http://localhost:5065, migra al iniciar en Development
```

Las versiones NuGet están centralizadas en `Directory.Packages.props` (los `.csproj` no llevan `Version`); EF Core va alineado a la versión que exige Npgsql (8.0.11). El SDK está fijado en `global.json`. Las dependencias Python van con versión exacta en `pyproject.toml`. CI (`.github/workflows/ci.yml`): build y test de ambos, imágenes Docker, Trivy y CodeQL.

## Estructura

- `src/server/Forensic.Domain`: agregados e invariantes. Sin paquetes externos; sólo BCL (SHA-256 incluido).
- `src/server/Forensic.Application`: casos de uso (`ServicioDeCasos`, `ServicioDeEvidencias`, `ServicioDeCustodia`) y puertos (`IPersistenciaForense`, `IAlmacenDeEvidencia`, `IWorkerDeIngesta`, `IActorActual`). El reloj es `TimeProvider`: nunca usar `DateTime.Now` ni un timestamp que venga del cliente.
- `src/server/Forensic.Infrastructure`: EF Core/Npgsql (`ContextoForense`, migraciones en `Persistencia/Migraciones`), cliente HTTP del worker (`Workers/WorkerDeIngestaHttp`, `WorkerIngesta:UrlBase`) y almacén de blobs en disco direccionado por hash (`raiz/ab/cd/<sha256>`, solo lectura).
- `src/server/Forensic.Api`: controllers (`Controllers/`). Las excepciones se mapean a ProblemDetails en `ManejadorDeErrores` (401, 403 acceso, 404, 409 conflicto de cadena, 422 regla de dominio o contenido rechazado por el worker, 423 integridad comprometida, 502 divergencia del control cruzado).
- `tests/Forensic.Domain.Tests` y `tests/Forensic.IntegrationTests` (`WebApplicationFactory` + Testcontainers `postgres:17`, entorno `Testing`). Ambos están en `Forensic.sln` aunque vivan fuera de `src/server`.
- `src/workers/ingest`: FastAPI. `POST /v1/analizar` recibe el archivo en crudo y devuelve SHA-256 + metadatos de ffprobe. Su hash es un control cruzado sobre **los mismos bytes crudos** que hashea el servidor: si el worker llegara a normalizar, reporta el hash del crudo de entrada, nunca el de su salida. La fuente de verdad es el hash que calcula el servidor al ingestar. `ServicioDeEvidencias` guarda el crudo, lo relee verificado del almacén y se lo pasa al worker; recién después crea la evidencia y el asiento, cuyo detalle guarda el hash del worker y los metadatos. Una divergencia (el worker reporta otro hash sobre bytes ya verificados) es un evento de seguridad: `DivergenciaDeControlCruzadoException`, `LogCritical`, 502 y no hay ingesta. Si el worker no responde, la ingesta procede con `controlCruzado.estado = "pendiente"` y se completa después con `POST .../evidencias/{id}/control-cruzado`, que agrega un asiento `ControlCruzadoCompletado` (decisión 5 de "Decisiones cerradas" en `docs/architecture.md`). Si ffprobe no reconoce el formato, la evidencia se custodia igual con `metadatos: null` y `motivoSinMetadatos`. ffprobe se invoca con una lista de argumentos sobre una ruta temporal propia, nunca con el nombre de la evidencia.

Detalles que no se ven a simple vista:
- **Identidad provisoria:** `ActorDeDesarrollo` lee las cabeceras `X-Usuario`/`X-Rol` sin autenticar. `Program.cs` se niega a arrancar fuera de `Development`/`Testing` hasta que exista el módulo de identidad (por eso el compose fuerza `Development`).
- **Asentar en la cadena:** leer `ObtenerUltimoAsientoAsync` → `RegistroDeCustodia.Asentar(...)` → `Agregar` la entidad y el asiento → un solo `GuardarCambiosAsync`. El índice único `(CasoId, Secuencia)` serializa las escrituras concurrentes: el perdedor recibe `ConflictoDeCustodiaException` (409).
- **Formato canónico del hash** (`AsientoDeCustodia.CalcularHash`, `forensic-video/custodia/v1`): campos con prefijo de longitud y timestamp UTC truncado a microsegundos (la precisión de PostgreSQL). Cambiarlo invalida todas las cadenas existentes: hay que versionarlo.
- **Triggers append-only:** están en la migración `Inicial` (SQL a mano) y bloquean UPDATE/DELETE/TRUNCATE en `AsientosDeCustodia` y `Evidencias`.
- `OperacionForense` y `Evidencia.Derivar` (doble vía) existen sólo en el dominio. La persistencia y los endpoints llegan con la Fase 2.

## Qué es

Sistema open-source, on-premise y multi-analista de análisis forense de video/imagen que reemplaza progresivamente a Amped FIVE. **La cadena de custodia es la arquitectura, no un módulo**: filtros e IA son plugins alrededor de un registro inmutable y reproducible de qué se le hizo a cada evidencia. Nada de ingeniería inversa ni binarios de Amped u otro software propietario.

## Arquitectura

Híbrida: el cómputo corre en la workstation del analista (GPU NVIDIA local); la custodia vive en un servidor central sin GPU que no procesa imágenes, sólo guarda, versiona y audita.

- **Cliente (workstation):** UI Avalonia (C#, decidido; WPF sólo si un control de terceros lo exige) → orquestador local → workers Python + FastAPI (OpenCV, PyTorch, Real-ESRGAN/SwinIR, YOLO, Tesseract/PaddleOCR). Los workers nunca se exponen fuera de la workstation.
- **Servidor de custodia:** .NET 8 + ASP.NET Core, monolito modular con bounded contexts (no microservicios: fragmentaría las transacciones de la cadena de custodia). API REST con authN/Z por rol y por caso, núcleo de dominio, servicio de auditoría (hash-chain). Cliente↔servidor por HTTPS/mTLS.
- **Persistencia:** tres cosas separadas que no se mezclan — PostgreSQL vía EF Core (casos/metadatos), almacén de blobs de evidencia (originales inmutables + SHA-256), y el log de custodia append-only encadenado.
- **Cola:** empezar con cola en PostgreSQL; RabbitMQ sólo si hace falta.
- **Ingesta:** sólo se hashean los bytes crudos y se leen metadatos (ffprobe, sólo lectura); **nunca se transcodifica en la ingesta**. La normalización de contenedores/códecs y la extracción de frames (celulares; Milestone MKV/AVI/MP4; HikVision) son Operaciones de la Fase 2 que producen derivados, no pasos de ingesta.
- **Identidad:** no hay Active Directory. Módulo de identidad propio en el servidor (roles analista/supervisor/auditor, Argon2 o bcrypt), con auditoría de accesos separada del log de custodia.

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

## Seguridad

La evidencia es entrada no confiable: validar tipo, tamaño y metadatos en la API. Consultas parametrizadas vía EF Core; nombres de archivo nunca interpolados en rutas ni comandos (relevante al invocar FFmpeg). Sin secretos en código ni imágenes Docker.

## Roadmap (el orden no es negociable)

1. **Cimiento de custodia** — servidor .NET + PostgreSQL, sin UI: ingesta + SHA-256 + log encadenado + gestión de casos. Gate: una evidencia se ingesta, hashea y audita de punta a punta (PoC: ingesta, hash, un registro encadenado, export del registro).
2. **Mejora clásica** — workers Python/OpenCV (deblur, denoise, geometría), cada operación asentada en el log. Gate: operación reproducible al 100%.
3. **Cliente de escritorio + GPU** — Avalonia, orquestador local.
4. **Analítica e IA** — super-resolución, YOLO, OCR, tracking; modelos versionados en Docker.
5. **Informe pericial + endurecimiento** — export de la cadena, CI/CD con SAST, escaneo de dependencias/imagen, firma de imágenes.

No construir filtros antes que la custodia.
