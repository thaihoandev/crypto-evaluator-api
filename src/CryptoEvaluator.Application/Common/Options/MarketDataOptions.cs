namespace CryptoEvaluator.Application.Common.Options;

public class MarketDataOptions
{
    public const string SectionName = "MarketData";

    /// <summary>Binance Futures REST API base URL (HTTP).</summary>
    public string Provider { get; set; } = "Binance";

    /// <summary>Binance Futures REST API base URL.</summary>
    public string BaseUrl { get; set; } = "https://fapi.binance.com";

    /// <summary>Binance Futures WebSocket base URL used by the ticker background service.</summary>
    public string WsBaseUrl { get; set; } = "wss://fstream.binance.com";

    /// <summary>
    /// Comma-separated list of lowercase symbols to subscribe to via WebSocket miniTicker.
    /// Example: "btcusdt,ethusdt,solusdt"
    /// </summary>
    public string WatchSymbols { get; set; } =
        "btcusdt,ethusdt,solusdt,bnbusdt,xrpusdt,nearusdt,adausdt,avaxusdt,dogeusdt,linkusdt,suiusdt,pepeusdt";

    public int TimeoutSeconds { get; set; } = 10;
    public int CacheTtlSeconds { get; set; } = 30;

    /// <summary>Parsed array of watch symbols from <see cref="WatchSymbols"/>.</summary>
    public string[] GetWatchSymbolArray() =>
        WatchSymbols
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToLowerInvariant())
            .ToArray();
}
