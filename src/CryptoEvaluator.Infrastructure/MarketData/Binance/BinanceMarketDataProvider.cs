using System.Net.Http.Json;
using System.Text.Json;
using CryptoEvaluator.Application.Common.Options;
using CryptoEvaluator.Application.MarketData.Interfaces;
using CryptoEvaluator.Domain.Enums;
using CryptoEvaluator.Domain.Models;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CryptoEvaluator.Infrastructure.MarketData.Binance;

public class BinanceMarketDataProvider : IMarketDataProvider
{
    private readonly HttpClient _httpClient;
    private readonly MarketDataOptions _options;
    private readonly IDistributedCache? _cache;
    private readonly ILogger<BinanceMarketDataProvider> _logger;

    public BinanceMarketDataProvider(
        HttpClient httpClient,
        IOptions<MarketDataOptions> options,
        ILogger<BinanceMarketDataProvider> logger,
        IDistributedCache? cache = null)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _cache = cache;

        if (_httpClient.BaseAddress == null && !string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            _httpClient.BaseAddress = new Uri(_options.BaseUrl);
        }
    }

    public async Task<IReadOnlyList<Candle>> GetCandlesAsync(
        string symbol,
        Timeframe timeframe,
        int limit = 250,
        CancellationToken cancellationToken = default)
    {
        string binanceSymbol = symbol.ToUpperInvariant().Trim();
        string interval = MapTimeframeToInterval(timeframe);
        string cacheKey = $"market:candles:{binanceSymbol}:{interval}:{limit}";

        if (_cache != null)
        {
            try
            {
                var cachedBytes = await _cache.GetAsync(cacheKey, cancellationToken);
                if (cachedBytes != null)
                {
                    var cachedCandles = JsonSerializer.Deserialize<List<Candle>>(cachedBytes);
                    if (cachedCandles != null && cachedCandles.Count > 0)
                    {
                        return cachedCandles;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read candles from distributed cache for {Symbol}", binanceSymbol);
            }
        }

        try
        {
            string requestUri = $"/fapi/v1/klines?symbol={binanceSymbol}&interval={interval}&limit={limit}";
            var response = await _httpClient.GetAsync(requestUri, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var jsonArray = await response.Content.ReadFromJsonAsync<JsonElement[][]>(cancellationToken: cancellationToken);
                if (jsonArray != null)
                {
                    var candles = new List<Candle>();
                    foreach (var elem in jsonArray)
                    {
                        long openTimeMs = elem[0].GetInt64();
                        decimal open = parseDecimal(elem[1]);
                        decimal high = parseDecimal(elem[2]);
                        decimal low = parseDecimal(elem[3]);
                        decimal close = parseDecimal(elem[4]);
                        decimal volume = parseDecimal(elem[5]);
                        long closeTimeMs = elem.Length > 6 ? elem[6].GetInt64() : openTimeMs;
                        decimal quoteVolume = elem.Length > 7 ? parseDecimal(elem[7]) : 0m;

                        DateTime openTime = DateTimeOffset.FromUnixTimeMilliseconds(openTimeMs).UtcDateTime;
                        DateTime closeTime = DateTimeOffset.FromUnixTimeMilliseconds(closeTimeMs).UtcDateTime;

                        candles.Add(new Candle(openTime, open, high, low, close, volume, closeTime, quoteVolume));
                    }

                    if (_cache != null && candles.Count > 0)
                    {
                        try
                        {
                            var bytes = JsonSerializer.SerializeToUtf8Bytes(candles);
                            await _cache.SetAsync(cacheKey, bytes, new DistributedCacheEntryOptions
                            {
                                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(_options.CacheTtlSeconds)
                            }, cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to cache candles for {Symbol}", binanceSymbol);
                        }
                    }

                    return candles;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch live market candles from Binance for {Symbol}. Fallback synthetic candles generated.", binanceSymbol);
        }

        // Fallback synthetic candle generation if external API call fails or times out
        return GenerateSyntheticCandles(binanceSymbol, limit);
    }

    public async Task<MarketTicker> GetTickerAsync(
        string symbol,
        CancellationToken cancellationToken = default)
    {
        string binanceSymbol = symbol.ToUpperInvariant().Trim();
        try
        {
            string requestUri = $"/fapi/v1/ticker/price?symbol={binanceSymbol}";
            var response = await _httpClient.GetAsync(requestUri, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var doc = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
                if (doc.TryGetProperty("price", out var priceElem))
                {
                    decimal price = decimal.Parse(priceElem.GetString() ?? "0");
                    return new MarketTicker(binanceSymbol, price, DateTime.UtcNow);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch ticker from Binance for {Symbol}", binanceSymbol);
        }

        return new MarketTicker(binanceSymbol, 100000m, DateTime.UtcNow);
    }

    public async Task<IReadOnlyList<CryptoSymbolInfo>> GetSymbolsAsync(CancellationToken cancellationToken = default)
    {
        string cacheKey = "market:symbols:exchangeInfo";
        if (_cache != null)
        {
            try
            {
                var cachedBytes = await _cache.GetAsync(cacheKey, cancellationToken);
                if (cachedBytes != null)
                {
                    var cachedSymbols = JsonSerializer.Deserialize<List<CryptoSymbolInfo>>(cachedBytes);
                    if (cachedSymbols != null && cachedSymbols.Count > 0)
                        return cachedSymbols;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read symbols from distributed cache");
            }
        }

        try
        {
            string requestUri = "/fapi/v1/exchangeInfo";
            var response = await _httpClient.GetAsync(requestUri, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var doc = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
                if (doc.TryGetProperty("symbols", out var symbolsArray) && symbolsArray.ValueKind == JsonValueKind.Array)
                {
                    var result = new List<CryptoSymbolInfo>();
                    foreach (var s in symbolsArray.EnumerateArray())
                    {
                        string status = s.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "";
                        string quoteAsset = s.TryGetProperty("quoteAsset", out var qa) ? qa.GetString() ?? "" : "";
                        string symbol = s.TryGetProperty("symbol", out var sym) ? sym.GetString() ?? "" : "";
                        string baseAsset = s.TryGetProperty("baseAsset", out var ba) ? ba.GetString() ?? "" : "";
                        string contractType = s.TryGetProperty("contractType", out var ct) ? ct.GetString() ?? "" : "";

                        if (status == "TRADING" && quoteAsset == "USDT" && contractType == "PERPETUAL")
                        {
                            result.Add(new CryptoSymbolInfo(
                                Symbol: symbol,
                                Name: $"{baseAsset} / USDT Perpetual",
                                BaseAsset: baseAsset,
                                QuoteAsset: quoteAsset));
                        }
                    }

                    if (result.Count > 0)
                    {
                        if (_cache != null)
                        {
                            try
                            {
                                var bytes = JsonSerializer.SerializeToUtf8Bytes(result);
                                await _cache.SetAsync(cacheKey, bytes, new DistributedCacheEntryOptions
                                {
                                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
                                }, cancellationToken);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to cache symbols");
                            }
                        }

                        return result;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch exchangeInfo from Binance Futures");
        }

        return GetFallbackSymbols();
    }

    private static IReadOnlyList<CryptoSymbolInfo> GetFallbackSymbols()
    {
        return new List<CryptoSymbolInfo>
        {
            new("BTCUSDT", "Bitcoin / USDT Perpetual", "BTC", "USDT"),
            new("ETHUSDT", "Ethereum / USDT Perpetual", "ETH", "USDT"),
            new("SOLUSDT", "Solana / USDT Perpetual", "SOL", "USDT"),
            new("BNBUSDT", "BNB / USDT Perpetual", "BNB", "USDT"),
            new("XRPUSDT", "Ripple / USDT Perpetual", "XRP", "USDT"),
            new("DOGEUSDT", "Dogecoin / USDT Perpetual", "DOGE", "USDT"),
            new("ADAUSDT", "Cardano / USDT Perpetual", "ADA", "USDT"),
            new("AVAXUSDT", "Avalanche / USDT Perpetual", "AVAX", "USDT"),
            new("LINKUSDT", "Chainlink / USDT Perpetual", "LINK", "USDT"),
            new("NEARUSDT", "Near / USDT Perpetual", "NEAR", "USDT"),
            new("SUIUSDT", "Sui / USDT Perpetual", "SUI", "USDT"),
            new("PEPEUSDT", "Pepe / USDT Perpetual", "PEPE", "USDT")
        };
    }

    private static decimal parseDecimal(JsonElement elem)
    {
        if (elem.ValueKind == JsonValueKind.String)
            return decimal.Parse(elem.GetString() ?? "0");
        return elem.GetDecimal();
    }

    private static string MapTimeframeToInterval(Timeframe timeframe) => timeframe switch
    {
        Timeframe.M1 => "1m",
        Timeframe.M5 => "5m",
        Timeframe.M15 => "15m",
        Timeframe.M30 => "30m",
        Timeframe.H1 => "1h",
        Timeframe.H4 => "4h",
        Timeframe.D1 => "1d",
        _ => "1h"
    };

    private static IReadOnlyList<Candle> GenerateSyntheticCandles(string symbol, int limit)
    {
        var candles = new List<Candle>();
        decimal basePrice = symbol.Contains("BTC") ? 104000m : (symbol.Contains("ETH") ? 3500m : 150m);
        DateTime now = DateTime.UtcNow.AddHours(-limit);

        Random rand = new Random(12345); // Deterministic seed for fallback
        decimal price = basePrice;

        for (int i = 0; i < limit; i++)
        {
            decimal change = (decimal)(rand.NextDouble() - 0.48) * (basePrice * 0.005m); // Slightly bullish drift
            decimal open = price;
            decimal close = price + change;
            decimal high = Math.Max(open, close) + (decimal)rand.NextDouble() * (basePrice * 0.002m);
            decimal low = Math.Min(open, close) - (decimal)rand.NextDouble() * (basePrice * 0.002m);
            decimal volume = 500m + (decimal)rand.NextDouble() * 1000m;

            DateTime openTime = now.AddHours(i);
            DateTime closeTime = openTime.AddHours(1).AddMilliseconds(-1);
            decimal quoteVolume = volume * close;

            candles.Add(new Candle(openTime, open, high, low, close, volume, closeTime, quoteVolume));
            price = close;
        }

        return candles;
    }
}
