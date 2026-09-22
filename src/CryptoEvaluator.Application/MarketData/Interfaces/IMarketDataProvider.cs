using CryptoEvaluator.Domain.Enums;
using CryptoEvaluator.Domain.Models;

namespace CryptoEvaluator.Application.MarketData.Interfaces;

public record CryptoSymbolInfo(
    string Symbol,
    string Name,
    string BaseAsset,
    string QuoteAsset);

public interface IMarketDataProvider
{
    Task<IReadOnlyList<Candle>> GetCandlesAsync(
        string symbol,
        Timeframe timeframe,
        int limit = 250,
        CancellationToken cancellationToken = default);

    Task<MarketTicker> GetTickerAsync(
        string symbol,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CryptoSymbolInfo>> GetSymbolsAsync(
        CancellationToken cancellationToken = default);
}
