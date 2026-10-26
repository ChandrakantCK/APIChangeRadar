# ---- Build stage: has the full .NET SDK ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json APIChangeRadar.slnx ./
COPY src/ src/
RUN dotnet restore src/Radar.Api/Radar.Api.csproj
RUN dotnet publish src/Radar.Api/Radar.Api.csproj -c Release -o /app --no-restore

# ---- Runtime stage: only the small ASP.NET runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID

ENTRYPOINT ["dotnet", "Radar.Api.dll"]