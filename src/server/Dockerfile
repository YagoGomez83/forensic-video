# Servidor de custodia. Contexto de build: la raíz del repo (necesita global.json y Directory.*.props).
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /repo
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/server/ src/server/
RUN dotnet publish src/server/Forensic.Api/Forensic.Api.csproj -c Release -o /publicar

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /publicar .

# Almacén de evidencia: montar un volumen aquí.
RUN mkdir -p /datos/evidencia && chown app:app /datos/evidencia
ENV AlmacenDeEvidencia__RutaRaiz=/datos/evidencia

USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "Forensic.Api.dll"]
