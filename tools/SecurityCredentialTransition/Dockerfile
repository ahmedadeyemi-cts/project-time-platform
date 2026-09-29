FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY SecurityCredentialTransition.csproj Program.cs ./
RUN dotnet publish -c Release --self-contained false -p:UseAppHost=false -o /publish

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /publish/ ./
USER $APP_UID
ENTRYPOINT ["dotnet", "SecurityCredentialTransition.dll"]
CMD ["verify"]
