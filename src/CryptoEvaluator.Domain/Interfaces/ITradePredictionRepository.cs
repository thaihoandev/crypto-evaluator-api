using CryptoEvaluator.Domain.Entities;

namespace CryptoEvaluator.Domain.Interfaces;

public interface ITradePredictionRepository
{
    Task<TradePrediction?> GetByTradeIdAsync(Guid tradeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TradeResult>> GetSimilarHistoricalTradesAsync(
        string symbol,
        decimal minScore,
        decimal maxScore,
        decimal minRr,
        decimal maxRr,
        CancellationToken cancellationToken = default);
    Task AddAsync(TradePrediction prediction, CancellationToken cancellationToken = default);
}
