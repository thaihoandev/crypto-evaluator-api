# Crypto Trade Evaluator API

Backend API hỗ trợ đánh giá thiết lập giao dịch tiền mã hoá. Hệ thống lấy dữ liệu từ Binance Futures, tính chỉ báo kỹ thuật, quản trị rủi ro, chấm điểm setup và mô phỏng dự báo Monte Carlo (GBM).

> Dự án phục vụ mục đích phân tích/tham khảo, **không phải lời khuyên đầu tư**.

## Tính năng

- Tạo, xem, lọc và đóng trade setup.
- Lấy ticker và danh sách cặp USDT Futures từ Binance.
- Phân tích thị trường để đề xuất setup `Long`, `Short` hoặc `Wait`.
- Tính EMA 20/50/200, RSI, ATR và tỷ lệ volume.
- Tính risk/reward, khối lượng vị thế, margin và điểm chất lượng setup.
- Dự báo xác suất thắng/thua bằng Monte Carlo Geometric Brownian Motion; lưu kết quả và hỗ trợ backtest prediction.
- Lưu dữ liệu bằng PostgreSQL; Redis dùng cho cache dữ liệu thị trường.
- Tài liệu API tương tác qua Scalar trong môi trường Development.

## Công nghệ

- .NET 10 / ASP.NET Core Web API
- Entity Framework Core + PostgreSQL
- Redis
- MediatR + FluentValidation
- Binance Futures REST API
- Scalar / OpenAPI
- xUnit cho unit test và integration test

## Cấu trúc dự án

```text
src/
├── CryptoEvaluator.API/             # HTTP API, middleware, OpenAPI
├── CryptoEvaluator.Application/     # Use case, nghiệp vụ phân tích và dự báo
├── CryptoEvaluator.Domain/          # Entity, enum, interface, model cốt lõi
└── CryptoEvaluator.Infrastructure/  # EF Core, PostgreSQL, Redis, Binance provider
tests/
├── CryptoEvaluator.Application.Tests/
├── CryptoEvaluator.Domain.Tests/
└── CryptoEvaluator.IntegrationTests/
```

## Yêu cầu

- [.NET SDK 10](https://dotnet.microsoft.com/download)
- Docker Desktop (khuyến nghị, để chạy PostgreSQL và Redis)
- Kết nối Internet để gọi Binance Futures

## Khởi chạy nhanh

### 1. Khởi chạy PostgreSQL và Redis

```bash
docker compose up -d
```

Mặc định Docker Compose mở PostgreSQL tại `localhost:5432` và Redis tại `localhost:6379`.

### 2. Cấu hình kết nối

Chuỗi kết nối mặc định nằm trong `src/CryptoEvaluator.API/appsettings.Development.json`. Để không commit mật khẩu thật, nên dùng User Secrets trên máy local:

```bash
dotnet user-secrets init --project src/CryptoEvaluator.API
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Database=crypto_evaluator;Username=postgres;Password=<mat_khau>" --project src/CryptoEvaluator.API
dotnet user-secrets set "ConnectionStrings:Redis" "localhost:6379" --project src/CryptoEvaluator.API
```

Nếu dùng `docker-compose.yml` nguyên bản, mật khẩu PostgreSQL là `postgrespassword`; hãy đặt giá trị User Secret tương ứng hoặc thay đổi thông tin trong Compose cho đồng bộ.

### 3. Restore, migration và chạy API

```bash
dotnet restore
dotnet ef database update --project src/CryptoEvaluator.Infrastructure --startup-project src/CryptoEvaluator.API
dotnet run --project src/CryptoEvaluator.API
```

API mặc định chạy tại `http://localhost:5099` (hoặc `https://localhost:7118`). Khi `ASPNETCORE_ENVIRONMENT=Development`, mở giao diện API tại:

```text
https://localhost:7118/scalar/v1
```

OpenAPI JSON: `https://localhost:7118/openapi/v1.json`.

## API chính

| Method | Endpoint | Mô tả |
| --- | --- | --- |
| `POST` | `/api/v1/trades` | Tạo trade setup |
| `GET` | `/api/v1/trades` | Danh sách trade, hỗ trợ filter |
| `GET` | `/api/v1/trades/{id}` | Chi tiết trade |
| `POST` | `/api/v1/trades/{id}/evaluate` | Đánh giá trade, chỉ báo và dự báo |
| `POST` | `/api/v1/trades/{id}/close` | Đóng trade, lưu journal |
| `GET` | `/api/v1/trades/{id}/prediction` | Kết quả prediction đã lưu |
| `GET` | `/api/v1/trades/predictions/backtest` | Backtest các prediction đã đóng |
| `GET` | `/api/v1/trades/ticker/{symbol}` | Giá hiện tại từ Binance |
| `GET` | `/api/v1/trades/symbols` | Các cặp USDT Futures được hỗ trợ |
| `GET` | `/api/v1/trades/analyze/{symbol}` | Phân tích thị trường và đề xuất setup |

Các enum được serialize dưới dạng chuỗi. Ví dụ giá trị thông dụng: `Long` / `Short`; timeframe: `M1`, `M5`, `M15`, `M30`, `H1`, `H4`, `D1`.

### Ví dụ tạo và đánh giá trade

```bash
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
```

Sau khi nhận `id` từ phản hồi:

```bash
curl -X POST http://localhost:5099/api/v1/trades/<id>/evaluate \
  -H "Content-Type: application/json" \
  -d '{ "randomSeed": 42 }'
```

### Ví dụ phân tích thị trường

```bash
curl "http://localhost:5099/api/v1/trades/analyze/BTCUSDT?timeframe=H1"
```

Phản hồi gồm snapshot chỉ báo, chất lượng dữ liệu, khuyến nghị, các lý do chặn setup và hai phương án Long/Short với entry, stop loss, take profit, risk/reward, điểm setup và xác suất dự báo.

## Kiểm thử

```bash
dotnet test
```

## Cấu hình đáng chú ý

- `MarketData`: URL Binance, timeout và TTL cache.
- `Prediction:DefaultSimulatedPaths`: số đường mô phỏng mặc định.
- `MarketAnalysis`: số candle yêu cầu, ATR multiplier, các ngưỡng điểm, xác suất thắng và risk/reward.
- CORS hiện cho phép frontend tại `http://localhost:3000`, `https://localhost:3000` và `http://localhost:5173`.

## Lưu ý bảo mật

- Không commit `appsettings.Local.json`, `.env`, User Secrets hoặc chuỗi kết nối production.
- Thay mật khẩu mặc định của PostgreSQL trước khi triển khai.
- Binance có thể giới hạn truy cập theo khu vực hoặc rate limit; API cần xử lý phù hợp khi triển khai production.

## License

Chưa có giấy phép được chỉ định. Hãy thêm tệp `LICENSE` trước khi công khai mã nguồn nếu cần.
