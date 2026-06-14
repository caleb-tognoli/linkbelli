# Multi-stage build for the Linkbelli API. Build context: the api/ directory.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore src/Linkbelli.Api/Linkbelli.Api.csproj
RUN dotnet publish src/Linkbelli.Api/Linkbelli.Api.csproj -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
# Pre-create the Data Protection key-ring mount point owned by the non-root user so a
# mounted volume (DataProtection:KeyRingPath) is writable when running as 'app'.
RUN mkdir -p /keys && chown app:app /keys
# Run as the non-root 'app' user shipped in the aspnet image to limit blast radius.
USER app
ENTRYPOINT ["dotnet", "Linkbelli.Api.dll"]
