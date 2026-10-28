# Servidor de custodia. Contexto de build: la raíz del repo (necesita global.json y Directory.*.props).
# Bases fijadas por digest; Dependabot propone las actualizaciones.
FROM mcr.microsoft.com/dotnet/sdk:8.0@sha256:6ba9644da07bb5b560a402acec9ca38711a0762969f1b61da261df5c9f6f85da AS build
WORKDIR /repo
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/server/ src/server/
RUN dotnet publish src/server/Forensic.Api/Forensic.Api.csproj -c Release -o /publicar

FROM mcr.microsoft.com/dotnet/aspnet:8.0@sha256:ea721098d6ea2894ff9bd8387821a1659d4765d6c014b8d648bb1da16f016f5a
WORKDIR /app
COPY --from=build /publicar .

# Almacén de evidencia: montar un volumen aquí.
RUN mkdir -p /datos/evidencia && chown app:app /datos/evidencia
ENV AlmacenDeEvidencia__RutaRaiz=/datos/evidencia

# Sellos de la cadena: otro volumen, separado de la base y de la evidencia. La clave privada no va en
# la imagen: llega como secreto montado (Custodia__Sellado__RutaClavePrivada).
RUN mkdir -p /datos/sellos && chown app:app /datos/sellos
ENV Custodia__Sellado__RaizDeSellos=/datos/sellos

USER app
# Sólo HTTPS con mTLS (Tls__*); los certificados llegan como secretos montados.
EXPOSE 8443
ENTRYPOINT ["dotnet", "Forensic.Api.dll"]
