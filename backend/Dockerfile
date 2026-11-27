FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore migration tooling before application sources invalidate the build layer.
COPY dotnet-tools.json .
RUN dotnet tool restore

COPY backend/HealthcareApp.Api/HealthcareApp.Api.csproj backend/HealthcareApp.Api/
COPY backend/HealthcareApp.Application/HealthcareApp.Application.csproj backend/HealthcareApp.Application/
COPY backend/HealthcareApp.Domain/HealthcareApp.Domain.csproj backend/HealthcareApp.Domain/
COPY backend/HealthcareApp.Infrastructure/HealthcareApp.Infrastructure.csproj backend/HealthcareApp.Infrastructure/
RUN dotnet restore backend/HealthcareApp.Api/HealthcareApp.Api.csproj

COPY backend/HealthcareApp.Api/ backend/HealthcareApp.Api/
COPY backend/HealthcareApp.Application/ backend/HealthcareApp.Application/
COPY backend/HealthcareApp.Domain/ backend/HealthcareApp.Domain/
COPY backend/HealthcareApp.Infrastructure/ backend/HealthcareApp.Infrastructure/
RUN dotnet publish backend/HealthcareApp.Api/HealthcareApp.Api.csproj \
    --configuration Release --no-restore --output /app/publish

FROM build AS migrations
ENTRYPOINT ["dotnet", "ef", "database", "update", "--project", "backend/HealthcareApp.Infrastructure", "--startup-project", "backend/HealthcareApp.Api", "--configuration", "Release", "--no-build"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "HealthcareApp.Api.dll"]
