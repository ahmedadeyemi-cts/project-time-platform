# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
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
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=5080 \
    DOTNET_EnableDiagnostics=0

EXPOSE 5080

COPY --from=build /app/publish/ ./

USER $APP_UID

ENTRYPOINT ["dotnet", "ProjectTime.Api.dll"]
