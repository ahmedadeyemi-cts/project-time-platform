# syntax=docker/dockerfile:1

# Guarded release controllers override both values with protected sha256 digest
# references. Defaults remain only for local and pre-existing non-release builds.
ARG PROJECTPULSE_DOTNET_SDK_BASE_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0
ARG PROJECTPULSE_DOTNET_ASPNET_BASE_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0

FROM ${PROJECTPULSE_DOTNET_SDK_BASE_IMAGE} AS build
WORKDIR /src

COPY src/backend/ProjectTime.Api/ProjectTime.Api.csproj src/backend/ProjectTime.Api/
RUN dotnet restore src/backend/ProjectTime.Api/ProjectTime.Api.csproj

COPY src/backend/ProjectTime.Api/ src/backend/ProjectTime.Api/
RUN dotnet publish src/backend/ProjectTime.Api/ProjectTime.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM ${PROJECTPULSE_DOTNET_ASPNET_BASE_IMAGE} AS runtime
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=5080 \
    DOTNET_EnableDiagnostics=0

EXPOSE 5080

COPY --from=build /app/publish/ ./

USER $APP_UID

ENTRYPOINT ["dotnet", "ProjectTime.Api.dll"]
