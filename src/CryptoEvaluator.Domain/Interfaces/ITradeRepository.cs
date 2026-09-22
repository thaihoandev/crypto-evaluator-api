using CryptoEvaluator.Domain.Entities;
using CryptoEvaluator.Domain.Enums;

namespace CryptoEvaluator.Domain.Interfaces;

public interface ITradeRepository
{
    Task<Trade?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Trade>> GetUserTradesAsync(
        Guid userId,
        string? symbol = null,
        TradeDirection? direction = null,
        TradeStatus? status = null,
        decimal? minScore = null,
        decimal? maxScore = null,
        CancellationToken cancellationToken = default);
    Task AddAsync(Trade trade, CancellationToken cancellationToken = default);
    void Update(Trade trade);
}
