# Multi-stage build for the Linkbelli API. Build context: the api/ directory.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Manifests first, so the restore layer survives a source change. Copying everything up front
# meant editing one .cs file re-resolved and re-downloaded the entire NuGet graph — on a project
# whose loop is "change something, rebuild the image".
# Directory.*.props first of all: central package management and the shared build settings live
# there, so a restore without them resolves nothing and fails.
COPY Directory.Build.props Directory.Packages.props Linkbelli.slnx ./
COPY src/Linkbelli.Api/Linkbelli.Api.csproj src/Linkbelli.Api/packages.lock.json src/Linkbelli.Api/
COPY src/Linkbelli.Application/Linkbelli.Application.csproj src/Linkbelli.Application/packages.lock.json src/Linkbelli.Application/
COPY src/Linkbelli.Contracts/Linkbelli.Contracts.csproj src/Linkbelli.Contracts/packages.lock.json src/Linkbelli.Contracts/
COPY src/Linkbelli.Core/Linkbelli.Core.csproj src/Linkbelli.Core/packages.lock.json src/Linkbelli.Core/
COPY src/Linkbelli.Infrastructure/Linkbelli.Infrastructure.csproj src/Linkbelli.Infrastructure/packages.lock.json src/Linkbelli.Infrastructure/
RUN dotnet restore src/Linkbelli.Api/Linkbelli.Api.csproj

COPY src/ src/
RUN dotnet publish src/Linkbelli.Api/Linkbelli.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# curl exists purely for the healthcheck below: the aspnet image ships neither it nor wget, and
# a check that cannot reach the app is not a check.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# /health covers the database too, so this reports ready rather than merely running — which is
# the distinction that matters here, because startup applies migrations and the container exists
# for some time before it can answer anything.
HEALTHCHECK --interval=10s --timeout=3s --start-period=60s --retries=6 \
    CMD curl -fsS http://localhost:8080/health || exit 1

# Pre-create the Data Protection key-ring mount point owned by the non-root user so a
# mounted volume (DataProtection:KeyRingPath) is writable when running as 'app'.
RUN mkdir -p /keys /thumbnails && chown app:app /keys /thumbnails
# Run as the non-root 'app' user shipped in the aspnet image to limit blast radius.
USER app
ENTRYPOINT ["dotnet", "Linkbelli.Api.dll"]
