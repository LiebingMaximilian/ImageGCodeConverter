# Container image for the ImageConverter web app.
# Build from the REPOSITORY ROOT (the app needs src/ImageConverter.Algorithms too):
#   docker build -f src/ImageConverter.Web/Dockerfile -t imageconverter .
#   docker run --rm -p 5080:8080 imageconverter          -> http://localhost:5080

# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# restore first (cached as long as the project files don't change)
COPY src/ImageConverter.Algorithms/ImageConverter.Algorithms.csproj src/ImageConverter.Algorithms/
COPY src/ImageConverter.Web/ImageConverter.Web.csproj src/ImageConverter.Web/
RUN dotnet restore src/ImageConverter.Web/ImageConverter.Web.csproj

COPY src/ImageConverter.Algorithms/ src/ImageConverter.Algorithms/
COPY src/ImageConverter.Web/ src/ImageConverter.Web/
RUN dotnet publish src/ImageConverter.Web/ImageConverter.Web.csproj \
        -c Release --no-restore -o /app /p:UseAppHost=false

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_NOLOGO=1
EXPOSE 8080

# non-root user that ships with the official .NET images (APP_UID=1654)
# hadolint ignore=DL3066
USER $APP_UID

# no curl in the image – a tiny HTTP request via bash is enough
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
  CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /api/schema HTTP/1.0\\r\\nHost: localhost\\r\\n\\r\\n' >&3 && head -n1 <&3 | grep -q ' 200 '"]

ENTRYPOINT ["dotnet", "ImageConverter.Web.dll"]
