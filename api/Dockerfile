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
ENTRYPOINT ["dotnet", "Linkbelli.Api.dll"]
