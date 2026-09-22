using CryptoEvaluator.Domain.Entities;

namespace CryptoEvaluator.Domain.Interfaces;

public interface ITradeEvaluationRepository
{
    Task<TradeEvaluation?> GetByTradeIdAsync(Guid tradeId, CancellationToken cancellationToken = default);
    Task<int> GetLatestVersionAsync(Guid tradeId, CancellationToken cancellationToken = default);
    Task AddAsync(TradeEvaluation evaluation, MarketSnapshot snapshot, CancellationToken cancellationToken = default);
}
