using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using CryptoEvaluator.Application.Common.Options;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CryptoEvaluator.Infrastructure.MarketData.Binance;

/// <summary>
/// Background service that maintains a persistent WebSocket connection to Binance Futures
/// miniTicker streams for commonly traded USDT perpetual pairs.
///
/// Keeps the distributed cache populated with real-time ticker prices so that
/// <see cref="BinanceMarketDataProvider.GetTickerAsync"/> can serve sub-millisecond
/// responses from cache instead of making outbound HTTP calls.
///
/// Binance WS endpoint: wss://fstream.binance.com/stream?streams=btcusdt@miniTicker/ethusdt@miniTicker/...
///
/// Behaviour:
///   - Auto-reconnect with exponential back-off (max 30 s)
///   - Responds to server ping frames with pong (Binance requirement)
///   - Reconnects after 23 hours (Binance terminates connections after 24 h)
///   - Graceful shutdown on cancellation
/// </summary>
public sealed class BinanceTickerWebSocketService : BackgroundService
{
    // ── Constants ────────────────────────────────────────────────────────────

    private const int MaxReconnectDelaySeconds = 30;
    private static readonly TimeSpan SessionRenewAfter = TimeSpan.FromHours(23);

    /// <summary>
    /// Cache key prefix for real-time ticker data written by this service.
    /// <see cref="BinanceMarketDataProvider"/> reads from the same keys.
    /// </summary>
    public const string CacheKeyPrefix = "market:ticker:ws:";

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    // ── Dependencies ─────────────────────────────────────────────────────────

    private readonly MarketDataOptions _options;
    private readonly IDistributedCache? _cache;
    private readonly RealtimeTickerStore _tickerStore;
    private readonly ILogger<BinanceTickerWebSocketService> _logger;
    private readonly ConcurrentDictionary<string, byte> _receivedSymbols =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _redisStoredSymbols =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _redisFailedSymbols =
        new(StringComparer.OrdinalIgnoreCase);

    public BinanceTickerWebSocketService(
        IOptions<MarketDataOptions> options,
        ILogger<BinanceTickerWebSocketService> logger,
        RealtimeTickerStore tickerStore,
        IDistributedCache? cache = null)
    {
        _options = options.Value;
        _logger = logger;
        _tickerStore = tickerStore;
        _cache = cache;
    }

    // ── BackgroundService ─────────────────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var watchSymbols = _options.GetWatchSymbolArray();
        _logger.LogInformation(
            "[BinanceWS] Ticker background service starting. WsBaseUrl={WsBaseUrl}, Watching {Count} symbols.",
            _options.WsBaseUrl, watchSymbols.Length);

        int attempt = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunSessionAsync(watchSymbols, stoppingToken);
                // Clean disconnect (session renewal) — reset backoff
                attempt = 0;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // Graceful shutdown
            }
            catch (Exception ex)
            {
                attempt++;
                int delaySeconds = Math.Min(
                    (int)(1 * Math.Pow(2, attempt - 1)),
                    MaxReconnectDelaySeconds);

                _logger.LogWarning(ex,
                    "[BinanceWS] Connection lost (attempt {Attempt}). Reconnecting in {Delay}s…",
                    attempt, delaySeconds);

                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
            }
        }

        _logger.LogInformation("[BinanceWS] Ticker background service stopped.");
    }

    // ── Session ───────────────────────────────────────────────────────────────

    private async Task RunSessionAsync(string[] watchSymbols, CancellationToken cancellationToken)
    {
        var streamNames = string.Join('/', watchSymbols.Select(s => $"{s}@miniTicker"));
        var wsBase = _options.WsBaseUrl.TrimEnd('/');
        var wsPath = wsBase.Contains("/stream") ? wsBase : $"{wsBase}/market/stream";
        var uri = new Uri($"{wsPath}?streams={streamNames}");

        using var ws = new ClientWebSocket();
        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);

        _logger.LogInformation("[BinanceWS] Connecting to {Uri}", uri);
        await ws.ConnectAsync(uri, cancellationToken);
        _logger.LogInformation("[BinanceWS] Connected. Streaming {Count} symbols.", watchSymbols.Length);

        // Session renewal: close after 23 h to avoid Binance's 24 h cutoff
        using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        sessionCts.CancelAfter(SessionRenewAfter);

        var buffer = new byte[8192];

        while (ws.State == WebSocketState.Open && !sessionCts.IsCancellationRequested)
        {
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), sessionCts.Token);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                _logger.LogInformation("[BinanceWS] Server sent close frame. Reconnecting…");
                break;
            }

            if (result.MessageType == WebSocketMessageType.Binary)
            {
                // Binance sends binary ping frames; respond with pong
                await ws.SendAsync(
                    new ArraySegment<byte>(buffer, 0, result.Count),
                    WebSocketMessageType.Binary,
                    true,
                    cancellationToken);
                continue;
            }

            if (!result.EndOfMessage)
            {
                // For very large messages, accumulate chunks
                await ProcessLargeMessageAsync(ws, buffer, result, cancellationToken);
                continue;
            }

            // Normal text message
            string json = Encoding.UTF8.GetString(buffer, 0, result.Count);
            await ProcessMessageAsync(json, cancellationToken);
        }

        // Session renewal path (not an error, just reconnect cleanly)
        if (sessionCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("[BinanceWS] 23-hour session limit reached. Renewing connection…");
            try
            {
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "session-renew", cancellationToken);
            }
            catch { /* ignore close errors */ }
        }
    }

    // ── Message processing ────────────────────────────────────────────────────

    private async Task ProcessLargeMessageAsync(
        ClientWebSocket ws,
        byte[] buffer,
        WebSocketReceiveResult firstResult,
        CancellationToken cancellationToken)
    {
        using var ms = new System.IO.MemoryStream();
        ms.Write(buffer, 0, firstResult.Count);

        WebSocketReceiveResult result;
        do
        {
            result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            ms.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        string json = Encoding.UTF8.GetString(ms.ToArray());
        await ProcessMessageAsync(json, cancellationToken);
    }

    private async Task ProcessMessageAsync(string json, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Combined stream wraps payload in { "stream": "...", "data": {...} }
            JsonElement data = root.TryGetProperty("data", out var d) ? d : root;

            // Check event type
            if (!data.TryGetProperty("e", out var eventType)) return;

            if (eventType.GetString() == "24hrMiniTicker")
            {
                await ProcessMiniTickerAsync(data, cancellationToken);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "[BinanceWS] Failed to parse message.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[BinanceWS] Error processing message.");
        }
    }

    private Task ProcessMiniTickerAsync(JsonElement data, CancellationToken cancellationToken)
    {
        if (!data.TryGetProperty("s", out var symElem)) return Task.CompletedTask;
        if (!data.TryGetProperty("c", out var priceElem)) return Task.CompletedTask;

        string symbol = symElem.GetString() ?? string.Empty;
        if (string.IsNullOrEmpty(symbol)) return Task.CompletedTask;

        if (!decimal.TryParse(priceElem.GetString(), out decimal price)) return Task.CompletedTask;

        long eventTimeMs = data.TryGetProperty("E", out var etElem) ? etElem.GetInt64() : 0;
        var ticker = new RealtimeTicker(
            Symbol: symbol,
            Price: price,
            Open24h: ParseDecimal(data, "o"),
            High24h: ParseDecimal(data, "h"),
            Low24h: ParseDecimal(data, "l"),
            Volume24h: ParseDecimal(data, "v"),
            QuoteVolume24h: ParseDecimal(data, "q"),
            EventTimeMs: eventTimeMs,
            UpdatedAt: DateTime.UtcNow);

        if (_receivedSymbols.TryAdd(symbol, 0))
            _logger.LogInformation("[BinanceWS] Received miniTicker for {Symbol}.", symbol);

        StoreTicker(ticker, cancellationToken);
        return Task.CompletedTask;
    }

    private readonly ConcurrentDictionary<string, DateTime> _lastRedisWrite = new(StringComparer.OrdinalIgnoreCase);

    private void StoreTicker(RealtimeTicker ticker, CancellationToken cancellationToken)
    {
        // 1. Instant update in local memory (0 network latency)
        _tickerStore.Set(ticker);

        // 2. Throttle Redis writes — only once per TickerRedisWriteIntervalSeconds per symbol.
        //    All same-process reads hit RealtimeTickerStore first (0 Redis reads).
        var cache = _cache;
        if (cache != null)
        {
            var now = DateTime.UtcNow;
            var lastWrite = _lastRedisWrite.TryGetValue(ticker.Symbol, out var dt) ? dt : DateTime.MinValue;
            var interval = TimeSpan.FromSeconds(Math.Max(1, _options.TickerRedisWriteIntervalSeconds));
            if (now - lastWrite >= interval)
            {
                _lastRedisWrite[ticker.Symbol] = now;
                _ = WriteDistributedTickerAsync(cache, ticker, cancellationToken);
            }
        }
    }

    private async Task WriteDistributedTickerAsync(
        IDistributedCache cache,
        RealtimeTicker ticker,
        CancellationToken cancellationToken)
    {
        try
        {
            string cacheKey = $"{CacheKeyPrefix}{ticker.Symbol.ToUpperInvariant()}";
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(ticker);
            int ttlSeconds = _options.TickerRedisCacheTtlSeconds > 0 ? _options.TickerRedisCacheTtlSeconds : 300;
            await cache.SetAsync(cacheKey, bytes, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(ttlSeconds)
            }, cancellationToken);

            if (_redisStoredSymbols.TryAdd(ticker.Symbol, 0))
                _logger.LogInformation("[BinanceWS] Cached miniTicker for {Symbol}.", ticker.Symbol);
        }
        catch (Exception ex)
        {
            if (_redisFailedSymbols.TryAdd(ticker.Symbol, 0))
                _logger.LogWarning(ex, "[BinanceWS] Cache write failed for {Symbol}.", ticker.Symbol);
        }
    }

    private static decimal ParseDecimal(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var p))
        {
            string? str = p.ValueKind == JsonValueKind.String ? p.GetString() : p.GetRawText();
            if (decimal.TryParse(str, out decimal val)) return val;
        }
        return 0m;
    }

}

/// <summary>Real-time ticker snapshot written to cache by <see cref="BinanceTickerWebSocketService"/>.</summary>
public record RealtimeTicker(
    string Symbol,
    decimal Price,
    decimal Open24h,
    decimal High24h,
    decimal Low24h,
    decimal Volume24h,
    decimal QuoteVolume24h,
    long EventTimeMs,
    DateTime UpdatedAt);
