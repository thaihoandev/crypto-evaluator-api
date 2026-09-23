# ── Stage 1: Build ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

# Copy solution & project files first for layer-cached restore
COPY *.slnx ./
COPY src/CryptoEvaluator.API/CryptoEvaluator.API.csproj              src/CryptoEvaluator.API/
COPY src/CryptoEvaluator.Application/CryptoEvaluator.Application.csproj  src/CryptoEvaluator.Application/
COPY src/CryptoEvaluator.Domain/CryptoEvaluator.Domain.csproj            src/CryptoEvaluator.Domain/
COPY src/CryptoEvaluator.Infrastructure/CryptoEvaluator.Infrastructure.csproj src/CryptoEvaluator.Infrastructure/

RUN dotnet restore src/CryptoEvaluator.API/CryptoEvaluator.API.csproj

# Copy all source and publish
COPY . .
RUN dotnet publish src/CryptoEvaluator.API/CryptoEvaluator.API.csproj \
    -c Release -o /app/publish --no-restore

# ── Stage 2: Runtime ───────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

# Render injects PORT automatically; ASP.NET reads ASPNETCORE_URLS
ENV ASPNETCORE_URLS=http://+:${PORT:-8080}
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

ENTRYPOINT ["dotnet", "CryptoEvaluator.API.dll"]
