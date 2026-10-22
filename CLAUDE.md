# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Estado del repositorio

Esqueleto de la Fase 1 (servidor de custodia .NET 8, sin UI ni workers). Todavía no hay módulo de identidad ni CI.

## Comandos

```bash
dotnet build                                   # warnings = errores (Directory.Build.props)
dotnet test                                    # todo; los de integración requieren Docker
dotnet test tests/ForensicVideo.Domain.Tests   # sólo dominio, sin Docker
dotnet test --filter "FullyQualifiedName~RegistroDeCustodiaTests.Eliminar_un_asiento_intermedio_se_detecta"

# Migraciones (dotnet-ef global). Revisar el SQL generado: los triggers van a mano.
dotnet ef migrations add <Nombre> --project src/ForensicVideo.Infrastructure --output-dir Persistencia/Migraciones

# Ejecutar en local
cp .env.example .env    # definir POSTGRES_PASSWORD
docker compose up -d    # PostgreSQL 17 en 127.0.0.1:5433
dotnet user-secrets set "ConnectionStrings:Forense" "Host=localhost;Port=5433;Database=forense;Username=forense;Password=<la de .env>" --project src/ForensicVideo.Api
dotnet run --project src/ForensicVideo.Api     # http://localhost:5065, migra al iniciar en Development
```

Las versiones de paquetes están centralizadas en `Directory.Packages.props` (los `.csproj` no llevan `Version`). EF Core va alineado a la versión que exige Npgsql (8.0.11). El SDK está fijado en `global.json`.

## Estructura de la solución

- `src/ForensicVideo.Domain`: agregados e invariantes. Sin paquetes externos; sólo BCL (SHA-256 incluido).
- `src/ForensicVideo.Application`: casos de uso (`ServicioDeCasos`, `ServicioDeEvidencias`, `ServicioDeCustodia`) y puertos (`IPersistenciaForense`, `IAlmacenDeEvidencia`, `IActorActual`). El reloj es `TimeProvider`: nunca usar `DateTime.Now` ni un timestamp que venga del cliente.
- `src/ForensicVideo.Infrastructure`: EF Core/Npgsql (`ContextoForense`, migraciones en `Persistencia/Migraciones`) y almacén de blobs en disco direccionado por hash (`raiz/ab/cd/<sha256>`, solo lectura).
- `src/ForensicVideo.Api`: minimal API (`Endpoints.cs`). Las excepciones se mapean a ProblemDetails en `ManejadorDeErrores` (403 acceso, 404, 409 conflicto de cadena, 422 regla de dominio, 423 integridad comprometida).
- `tests/ForensicVideo.IntegrationTests`: `WebApplicationFactory` + Testcontainers (`postgres:17`), entorno `Testing`.

Detalles que no se ven a simple vista:
- **Identidad provisoria:** `ActorDeDesarrollo` lee las cabeceras `X-Usuario`/`X-Rol` sin autenticar. `Program.cs` se niega a arrancar fuera de `Development`/`Testing` hasta que exista el módulo de identidad.
- **Asentar en la cadena:** leer `ObtenerUltimoAsientoAsync` → `RegistroDeCustodia.Asentar(...)` → `Agregar` la entidad y el asiento → un solo `GuardarCambiosAsync`. El índice único `(CasoId, Secuencia)` serializa las escrituras concurrentes: el perdedor recibe `ConflictoDeCustodiaException` (409).
- **Formato canónico del hash** (`AsientoDeCustodia.CalcularHash`, `forensic-video/custodia/v1`): campos con prefijo de longitud y timestamp UTC truncado a microsegundos (la precisión de PostgreSQL). Cambiarlo invalida todas las cadenas existentes: hay que versionarlo.
- **Triggers append-only:** están en la migración `Inicial` (SQL a mano) y bloquean UPDATE/DELETE/TRUNCATE en `AsientosDeCustodia` y `Evidencias`.
- `OperacionForense` y `Evidencia.Derivar` (doble vía) existen sólo en el dominio. La persistencia y los endpoints llegan con la Fase 2.

La fuente de diseño es `../Sistema Forense de Imagen — Arquitectura y Roadmap (1).pdf` (la versión `(1)` es la más reciente; incluye las decisiones cerradas). Lo que sigue resume lo que condiciona el código.

## Qué es

Sistema open-source, on-premise y multi-analista de análisis forense de video/imagen que reemplaza progresivamente a Amped FIVE. **La cadena de custodia es la arquitectura, no un módulo**: filtros e IA son plugins alrededor de un registro inmutable y reproducible de qué se le hizo a cada evidencia. Nada de ingeniería inversa ni binarios de Amped u otro software propietario.

## Arquitectura

Híbrida: el cómputo corre en la workstation del analista (GPU NVIDIA local); la custodia vive en un servidor central sin GPU que no procesa imágenes, sólo guarda, versiona y audita.

- **Cliente (workstation):** UI Avalonia (C#, decidido; WPF sólo si un control de terceros lo exige) → orquestador local → workers Python + FastAPI (OpenCV, PyTorch, Real-ESRGAN/SwinIR, YOLO, Tesseract/PaddleOCR). Los workers nunca se exponen fuera de la workstation.
- **Servidor de custodia:** .NET 8 + ASP.NET Core, monolito modular con bounded contexts (no microservicios: fragmentaría las transacciones de la cadena de custodia). API REST con authN/Z por rol y por caso, núcleo de dominio, servicio de auditoría (hash-chain). Cliente↔servidor por HTTPS/mTLS.
- **Persistencia:** tres cosas separadas que no se mezclan — PostgreSQL vía EF Core (casos/metadatos), almacén de blobs de evidencia (originales inmutables + SHA-256), y el log de custodia append-only encadenado.
- **Cola:** empezar con cola en PostgreSQL; RabbitMQ sólo si hace falta.
- **Ingesta:** FFmpeg para normalizar contenedores/códecs y extraer frames (celulares; Milestone MKV/AVI/MP4; HikVision), preservando siempre el original y su hash.
- **Identidad:** no hay Active Directory. Módulo de identidad propio en el servidor (roles analista/supervisor/auditor, Argon2 o bcrypt), con auditoría de accesos separada del log de custodia.

Clean Architecture + DDD: la capa de dominio no referencia OpenCV, PyTorch, EF Core ni ASP.NET. Si una regla de custodia importa algo de infraestructura, es un error de diseño.

## Modelo de dominio

Agregados: `Caso`, `Evidencia`, `OperacionForense`, `RegistroDeCustodia`.

Invariantes no negociables:
1. Toda evidencia se hashea (SHA-256) antes de cualquier otra cosa; sin hash no hay ingesta. Se verifica el hash en cada lectura; discrepancia = alerta y bloqueo.
2. Ninguna operación modifica el original: siempre produce un derivado nuevo con su propio hash, que referencia a su padre.
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
