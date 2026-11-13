FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY backend/HealthcareApp.Api/HealthcareApp.Api.csproj backend/HealthcareApp.Api/
RUN dotnet restore backend/HealthcareApp.Api/HealthcareApp.Api.csproj

COPY backend/HealthcareApp.Api/ backend/HealthcareApp.Api/
RUN dotnet publish backend/HealthcareApp.Api/HealthcareApp.Api.csproj \
    --configuration Release --no-restore --output /app/publish

FROM build AS migrations
COPY dotnet-tools.json .
RUN dotnet tool restore
ENTRYPOINT ["dotnet", "ef", "database", "update", "--project", "backend/HealthcareApp.Api", "--configuration", "Release", "--no-build"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "HealthcareApp.Api.dll"]
