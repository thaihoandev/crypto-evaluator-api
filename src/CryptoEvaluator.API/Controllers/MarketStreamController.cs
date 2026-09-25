using CryptoEvaluator.Infrastructure.MarketData.Binance;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using System.Text;
using System.Text.Json;

namespace CryptoEvaluator.API.Controllers;

/// <summary>
/// Exposes real-time Binance ticker data via:
///   - GET /api/v1/market/ticker/{symbol}    — single ticker snapshot from WS cache
///   - GET /api/v1/market/tickers            — multi-symbol snapshot
///   - GET /api/v1/market/ticker/{symbol}/sse — Server-Sent Events stream (push updates every 500ms)
///
/// Data is sourced from the <see cref="BinanceTickerWebSocketService"/> WS cache,
/// falling back gracefully when the cache entry is not yet available.
/// </summary>
[ApiController]
[Route("api/v1/market")]
public class MarketStreamController : ControllerBase
{
    private static readonly string[] DefaultSymbols =
    [
        "BTCUSDT", "ETHUSDT", "SOLUSDT", "BNBUSDT", "XRPUSDT",
        "NEARUSDT", "ADAUSDT", "AVAXUSDT", "DOGEUSDT", "LINKUSDT",
        "SUIUSDT", "PEPEUSDT"
    ];

    private readonly IDistributedCache _cache;
    private readonly RealtimeTickerStore _tickerStore;
    private readonly ILogger<MarketStreamController> _logger;

    public MarketStreamController(
        IDistributedCache cache,
        RealtimeTickerStore tickerStore,
        ILogger<MarketStreamController> logger)
    {
        _cache = cache;
        _tickerStore = tickerStore;
        _logger = logger;
    }

    /// <summary>
    /// Get real-time ticker snapshot for a single symbol from the WS cache.
    /// </summary>
    [HttpGet("ticker/{symbol}")]
    [ProducesResponseType(typeof(RealtimeTicker), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetTicker(string symbol, CancellationToken cancellationToken)
    {
        var ticker = await GetCachedTicker(symbol.ToUpperInvariant(), cancellationToken);
        if (ticker == null)
            return NotFound(new { code = "TICKER_NOT_AVAILABLE", message = $"No real-time data for {symbol}. WS stream may still be initializing." });

        return Ok(ticker);
    }

    /// <summary>
    /// Get real-time ticker snapshots for all tracked symbols.
    /// </summary>
    [HttpGet("tickers")]
    [ProducesResponseType(typeof(IReadOnlyList<RealtimeTicker>), 200)]
    public async Task<IActionResult> GetTickers(
        [FromQuery] string? symbols,
        CancellationToken cancellationToken)
    {
        string[] symbolList = string.IsNullOrWhiteSpace(symbols)
            ? DefaultSymbols
            : symbols.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Select(s => s.ToUpperInvariant())
                     .ToArray();

        var tasks = symbolList.Select(s => GetCachedTicker(s, cancellationToken));
        var results = await Task.WhenAll(tasks);

        return Ok(results.Where(t => t != null).ToList());
    }

    /// <summary>
    /// Server-Sent Events endpoint for real-time ticker push updates.
    /// Useful for environments where direct Binance WebSocket is blocked.
    ///
    /// Usage (JavaScript):
    ///   const evtSource = new EventSource('/api/v1/market/ticker/BTCUSDT/sse');
    ///   evtSource.onmessage = (e) => console.log(JSON.parse(e.data));
    /// </summary>
    [HttpGet("ticker/{symbol}/sse")]
    public async Task StreamTicker(string symbol, CancellationToken cancellationToken)
    {
        Response.Headers["Content-Type"] = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["Connection"] = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no"; // Disable Nginx buffering

        string upperSymbol = symbol.ToUpperInvariant();
        _logger.LogInformation("[SSE] Client connected for {Symbol}", upperSymbol);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var ticker = await GetCachedTicker(upperSymbol, cancellationToken);

                if (ticker != null)
                {
                    string json = JsonSerializer.Serialize(ticker);
                    string ssePayload = $"data: {json}\n\n";
                    byte[] bytes = Encoding.UTF8.GetBytes(ssePayload);
                    await Response.Body.WriteAsync(bytes, cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                }

                // Poll cache every 500ms — cache is updated by WS service in real-time
                await Task.Delay(500, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected
        }
        finally
        {
            _logger.LogInformation("[SSE] Client disconnected for {Symbol}", upperSymbol);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<RealtimeTicker?> GetCachedTicker(string symbol, CancellationToken cancellationToken)
    {
        // The WebSocket writes here before attempting Redis. This keeps the
        // real-time endpoint responsive if the distributed cache is slow.
        if (_tickerStore.TryGet(symbol, out var localTicker))
            return localTicker;

        try
        {
            string cacheKey = $"{BinanceTickerWebSocketService.CacheKeyPrefix}{symbol}";
            var bytes = await _cache.GetAsync(cacheKey, cancellationToken);
            if (bytes != null)
                return JsonSerializer.Deserialize<RealtimeTicker>(bytes);

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Cache read failed for {Symbol}", symbol);
            return _tickerStore.TryGet(symbol, out var ticker) ? ticker : null;
        }
    }
}
