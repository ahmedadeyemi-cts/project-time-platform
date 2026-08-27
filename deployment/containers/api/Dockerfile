# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

USER root

# The API publish uses a bounded Python source post-processor for legacy Word
# extraction. Keep Python in the disposable SDK build stage only; it is not
# copied into runtime.
RUN apt-get update \
    && apt-get install -y --no-install-recommends python3 \
    && python3 --version \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /src

COPY src/backend/ProjectTime.Api/ProjectTime.Api.csproj src/backend/ProjectTime.Api/
RUN dotnet restore src/backend/ProjectTime.Api/ProjectTime.Api.csproj

COPY src/backend/ProjectTime.Api/ src/backend/ProjectTime.Api/
# FlowHive's protected-Test SOW is a legacy binary .doc. The canonical extractor
# stays reviewable in Git, while the container build applies the existing
# fail-closed OLE signature and antiword routing transform before MSBuild creates
# its compiler copy. This keeps .doc parsing private and bounded without changing
# the tracked canonical source during ordinary builds.
RUN python3 src/backend/ProjectTime.Api/build/enable-flowhive-legacy-word-extraction.py \
        --path src/backend/ProjectTime.Api/Ai/PulseAiPrivateDocumentExtractionService.cs \
    && grep -Fq '".doc" => await PulseAiLegacyBinaryWordExtraction.ExtractAsync' \
        src/backend/ProjectTime.Api/Ai/PulseAiPrivateDocumentExtractionService.cs \
    && grep -Fq '".doc" => detected == "ole_compound_word"' \
        src/backend/ProjectTime.Api/Ai/PulseAiPrivateDocumentExtractionService.cs
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
