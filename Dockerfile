# syntax=docker/dockerfile:1

# One container image, one Deployment, three listeners (spec §2).
#
# Build:  docker build -t corp-identity-proxy .
# Run:    docker run -p 8080:8080 -p 8082:8082 \
#           -v $PWD/config/routes:/app/config/routes:ro \
#           -v $PWD/config/environments/dev.yaml:/app/config/environment/environment.yaml:ro \
#           corp-identity-proxy

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so dependency layers are cached across source changes.
COPY global.json Directory.Build.props Directory.Packages.props Proxy.slnx ./
COPY src/Proxy.Host/Proxy.Host.csproj                   src/Proxy.Host/
COPY src/Proxy.Core/Proxy.Core.csproj                   src/Proxy.Core/
COPY src/Proxy.Auth/Proxy.Auth.csproj                   src/Proxy.Auth/
COPY src/Proxy.Config/Proxy.Config.csproj               src/Proxy.Config/
COPY src/Proxy.Status/Proxy.Status.csproj               src/Proxy.Status/
COPY src/Proxy.Observability/Proxy.Observability.csproj src/Proxy.Observability/
RUN dotnet restore src/Proxy.Host/Proxy.Host.csproj

COPY src/ src/
RUN dotnet publish src/Proxy.Host/Proxy.Host.csproj --no-restore --configuration Release --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Ports are bound explicitly by the host (spec §2); the base image's default must not add a fourth.
ENV ASPNETCORE_HTTP_PORTS=""

# Where the ConfigMaps are mounted (spec §4.3). The Helm chart mounts both paths read-only.
ENV PROXY_Config__RoutesDirectory=/app/config/routes
ENV PROXY_Config__EnvironmentFile=/app/config/environment/environment.yaml

COPY --from=build /app/publish ./

EXPOSE 8080 8081 8082

# Non-root; the aspnet image defines this user. Nothing is written to disk at runtime.
USER $APP_UID
ENTRYPOINT ["dotnet", "Proxy.Host.dll"]
