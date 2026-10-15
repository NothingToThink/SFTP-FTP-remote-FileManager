# Сборка из корня репозитория: docker build -f deploy/sync-server/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Core/ Core/
COPY ServerBackend/ ServerBackend/
RUN dotnet publish ServerBackend/ServerBackend.csproj -c Release -o /out --nologo

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
# app.db, app.key и jwt.key (создаются при первом старте) живут на томе
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080 \
    SWINE_DATA_DIR=/data
VOLUME /data
EXPOSE 8080
ENTRYPOINT ["dotnet", "ServerBackend.dll"]
