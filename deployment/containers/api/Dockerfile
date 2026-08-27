# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

USER root

# Directory.Build.targets invokes a Python 3 source generator during publish.
# Keep Python in the disposable SDK build stage only; it is not copied into runtime.
RUN apt-get update \
    && apt-get install -y --no-install-recommends python3 \
    && python3 --version \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /src

COPY src/backend/ProjectTime.Api/ProjectTime.Api.csproj src/backend/ProjectTime.Api/
RUN dotnet restore src/backend/ProjectTime.Api/ProjectTime.Api.csproj

COPY src/backend/ProjectTime.Api/ src/backend/ProjectTime.Api/
RUN dotnet publish src/backend/ProjectTime.Api/ProjectTime.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

USER root

# FlowHive may receive legacy binary Microsoft Word .doc SOWs that remain
# downloadable in Modules 055C and 019. antiword provides a small, local,
# text-only converter for those files. It never receives network access or
# executes document macros; the application still requires the private malware
# scan and path-confinement gates before invoking it.
RUN apt-get update \
    && apt-get install -y --no-install-recommends antiword \
    && (antiword -h >/dev/null 2>&1 || true) \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=5080 \
    DOTNET_EnableDiagnostics=0 \
    PROJECTPULSE_LEGACY_WORD_EXTRACTOR_PATH=/usr/bin/antiword

EXPOSE 5080

COPY --from=build /app/publish/ ./

USER $APP_UID

ENTRYPOINT ["dotnet", "ProjectTime.Api.dll"]
