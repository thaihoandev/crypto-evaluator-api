# HAWK Pulse — Crypto Trade Evaluator · Backend API

> .NET 10 Web API powering quantitative trade evaluation, Monte Carlo prediction, and AI-driven market analysis for the HAWK Pulse platform.

---

## Overview

The **Crypto Evaluator API** is the backend service of the HAWK Pulse platform. It fetches live OHLCV data from Binance Futures, computes technical indicators, scores trade setups using a multi-rule quantitative engine, runs Monte Carlo GBM simulations for win/loss probability forecasts, and exposes a RESTful API consumed by the React frontend.

> This project is for analytical and educational purposes — **not investment advice**.

---

## Tech Stack

| Layer | Technology |
|---|---|
| Runtime | .NET 10 / ASP.NET Core Web API |
| Architecture | Clean Architecture (Domain / Application / Infrastructure / API) |
| CQRS | MediatR |
| Validation | FluentValidation |
| ORM | Entity Framework Core 9 |
| Database | PostgreSQL 16 |
| Cache | Redis 7 |
| Market Data | Binance Futures REST API (`fapi.binance.com`) |
| API Docs | Scalar + OpenAPI |
| Testing | xUnit (Unit + Integration) |
| Containerization | Docker / Docker Compose |

---

## Features

### 📊 Trade Management
- Create, list, filter, and close trade setups
- Full trade journal with open/closed state tracking
- Re-evaluate any historical trade with a fresh market snapshot

### 🔬 Quantitative Evaluation Engine
- Fetches OHLCV candles from Binance Futures for the configured timeframe
- Computes **EMA 20/50/200**, **RSI**, **ATR**, and **volume ratio**
- Calculates **risk/reward ratio**, position size, margin, and liquidation levels
- Scores each trade setup against a multi-rule rubric (trend alignment, momentum, volatility, structure, R:R) via `TradeScoreCalculator`

### 🎲 Monte Carlo Prediction (GBM)
- Runs Geometric Brownian Motion simulation with configurable simulated paths (default: **10,000**)
- Returns win probability, loss probability, no-hit probability, and expected R-multiple
- Stores prediction results per trade for later backtest comparison

### 🤖 AI Market Analyzer
- Analyzes any symbol + timeframe on demand
- Detects market structure (pivot highs/lows) using `MarketStructureAnalyzer`
- Proposes **Long** / **Short** setups with entry, SL (ATR-based), TP (R:R-based), and setup score
- Returns `Wait` when market conditions don't meet minimum quality thresholds

### 📈 Prediction Backtest
- Compares saved Monte Carlo predictions against actual trade outcomes for all closed trades
- Returns hit-rate and corridor statistics

### ⚡ Caching
- Redis cache for Binance ticker data with configurable TTL (default: **30s**)
- Reduces Binance API call frequency under high frontend traffic

---

## Getting Started

### Prerequisites
- [.NET SDK 10](https://dotnet.microsoft.com/download)
- **Docker Desktop** (for PostgreSQL and Redis)
- Internet access to reach Binance Futures API

### 1. Start Infrastructure

Spin up PostgreSQL 16 and Redis 7 with Docker Compose:

```bash
docker compose up -d
```

| Service | Default Port |
|---|---|
| PostgreSQL | `localhost:5432` |
| Redis | `localhost:6379` |

Default credentials: `POSTGRES_USER=postgres`, `POSTGRES_PASSWORD=postgrespassword`, `POSTGRES_DB=crypto_evaluator`.

### 2. Configure Connection Strings

Use **User Secrets** to avoid committing credentials:

```bash
dotnet user-secrets init --project src/CryptoEvaluator.API

dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
  "Host=localhost;Database=crypto_evaluator;Username=postgres;Password=postgrespassword" \
  --project src/CryptoEvaluator.API

dotnet user-secrets set "ConnectionStrings:Redis" "localhost:6379" \
  --project src/CryptoEvaluator.API
```

### 3. Apply Migrations & Run

```bash
dotnet restore

dotnet ef database update \
  --project src/CryptoEvaluator.Infrastructure \
  --startup-project src/CryptoEvaluator.API

dotnet run --project src/CryptoEvaluator.API
```

The API starts at:
- HTTP: `http://localhost:5099`
- HTTPS: `https://localhost:7118`

**Interactive API Docs** (Development only):
```
https://localhost:7118/scalar/v1
```

**OpenAPI JSON Schema:**
```
https://localhost:7118/openapi/v1.json
```

---

## API Reference

### Trades

| Method | Endpoint | Description |
|---|---|---|
| `POST` | `/api/v1/trades` | Create a new trade setup |
| `GET` | `/api/v1/trades` | List trades (supports filters) |
| `GET` | `/api/v1/trades/{id}` | Get trade details |
| `POST` | `/api/v1/trades/{id}/evaluate` | Evaluate trade — fetch candles, compute indicators, score, run Monte Carlo |
| `POST` | `/api/v1/trades/{id}/close` | Close trade and record outcome |
| `GET` | `/api/v1/trades/{id}/prediction` | Retrieve stored prediction result |
| `GET` | `/api/v1/trades/predictions/backtest` | Backtest all closed trade predictions |

### Market Data

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/api/v1/trades/ticker/{symbol}` | Live price from Binance (Redis-cached) |
| `GET` | `/api/v1/trades/symbols` | Supported USDT Futures pairs |
| `GET` | `/api/v1/trades/analyze/{symbol}?timeframe=H1` | AI market analysis & setup proposal |

**Enum string values:**
- Direction: `Long` / `Short`
- Timeframe: `M1`, `M5`, `M15`, `M30`, `H1`, `H4`, `D1`

### Example: Create & Evaluate a Trade

```bash
# 1. Create trade setup
curl -X POST http://localhost:5099/api/v1/trades \
  -H "Content-Type: application/json" \
  -d '{
    "symbol": "BTCUSDT",
    "direction": "Long",
    "timeframe": "H1",
    "entryPrice": 65000,
    "stopLoss": 64000,
    "takeProfit": 67000,
    "accountBalance": 10000,
    "riskPercent": 1,
    "leverage": 5
  }'

# 2. Evaluate (use the id returned above)
curl -X POST http://localhost:5099/api/v1/trades/<id>/evaluate \
  -H "Content-Type: application/json" \
  -d '{ "randomSeed": 42 }'
```

### Example: AI Market Analysis

```bash
curl "http://localhost:5099/api/v1/trades/analyze/BTCUSDT?timeframe=H1"
```

Response includes: indicator snapshot, data quality, recommendation (`Long` / `Short` / `Wait`), blocking reasons (if any), and two proposed setups (Long & Short) with entry, SL, TP, R:R, setup score, and Monte Carlo win probability.

---

## Docker Deployment

A multi-stage `Dockerfile` is included for production builds:

```bash
# Build image
docker build -t crypto-evaluator-api .

# Run container (ensure DB and Redis are reachable)
docker run -p 8080:8080 \
  -e ConnectionStrings__DefaultConnection="Host=<db>;..." \
  -e ConnectionStrings__Redis="<redis>:6379" \
  crypto-evaluator-api
```

The image uses `mcr.microsoft.com/dotnet/aspnet:10.0` as the runtime base. The `PORT` environment variable is respected (injected automatically on platforms like **Render**).

---

## Configuration Reference

Key settings in `appsettings.json`:

| Section | Key | Default | Description |
|---|---|---|---|
| `MarketData` | `BaseUrl` | `https://fapi.binance.com` | Binance Futures REST base URL |
| `MarketData` | `CacheTtlSeconds` | `30` | Redis ticker cache TTL |
| `MarketData` | `TimeoutSeconds` | `10` | Binance HTTP request timeout |
| `Prediction` | `DefaultSimulatedPaths` | `10000` | Monte Carlo simulation paths |
| `MarketAnalysis` | `MinimumSetupScore` | `65` | Min score to propose a setup |
| `MarketAnalysis` | `MinimumWinProbability` | `55` | Min Monte Carlo win % to propose |
| `MarketAnalysis` | `StopLossAtrMultiplier` | `1.5` | ATR multiplier for SL placement |
| `MarketAnalysis` | `TakeProfitRiskRewardRatio` | `2` | R:R ratio for TP placement |
| `Cors` | `AllowedOrigins` | Vercel UI URL | Allowed CORS origins |

---

## Testing

```bash
dotnet test
```

Test projects:

| Project | Scope |
|---|---|
| `CryptoEvaluator.Domain.Tests` | Domain entity logic |
| `CryptoEvaluator.Application.Tests` | Scoring, indicators, Monte Carlo, market analysis |
| `CryptoEvaluator.IntegrationTests` | Full API pipeline with real DB |

---

## Security Notes

- **Never commit** `appsettings.Local.json`, `.env`, User Secrets, or production connection strings.
- Change the default PostgreSQL password (`postgrespassword`) before any production deployment.
- Binance may apply regional restrictions or rate limits — ensure proper retry/fallback handling in production.
- CORS is currently configured for the Vercel-hosted frontend and local dev origins (`localhost:3000`, `localhost:5173`).

---

## Author

Developed by [@thaihoandev](https://github.com/thaihoandev)
© 2026 HAWK Pulse
