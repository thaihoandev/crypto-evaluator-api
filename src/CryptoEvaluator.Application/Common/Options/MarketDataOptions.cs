namespace CryptoEvaluator.Application.Common.Options;

public class MarketDataOptions
{
    public const string SectionName = "MarketData";

    public string Provider { get; set; } = "Binance";
    public string BaseUrl { get; set; } = "https://fapi.binance.com";
    public int TimeoutSeconds { get; set; } = 10;
    public int CacheTtlSeconds { get; set; } = 30;
}
