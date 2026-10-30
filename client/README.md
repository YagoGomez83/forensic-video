# Cliente de escritorio

Avalonia 11 (C#, MVVM). Una ventana al servidor de custodia: no tiene lógica forense propia.
Ver `CLAUDE.md` y `docs/architecture.md`.

```bash
dotnet build client/Forensic.Cliente.sln
dotnet test client/Forensic.Cliente.sln
dotnet run --project client/Forensic.Cliente
```

Configuración: `C:\ProgramData\ForensicVideo\cliente.json` o, si no existe, las variables `FORENSIC_*`
(ver `CLAUDE.md`). Instalación en una workstation: `docs/cliente-instalacion.md`.
