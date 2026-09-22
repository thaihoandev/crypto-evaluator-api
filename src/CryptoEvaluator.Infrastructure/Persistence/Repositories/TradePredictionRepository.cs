using CryptoEvaluator.Domain.Entities;
using CryptoEvaluator.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CryptoEvaluator.Infrastructure.Persistence.Repositories;

public class TradePredictionRepository : ITradePredictionRepository
{
    private readonly AppDbContext _context;

    public TradePredictionRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<TradePrediction?> GetByTradeIdAsync(Guid tradeId, CancellationToken cancellationToken = default)
    {
        return await _context.TradePredictions
            .Where(p => p.TradeId == tradeId)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TradeResult>> GetSimilarHistoricalTradesAsync(
        string symbol,
        decimal minScore,
        decimal maxScore,
        decimal minRr,
        decimal maxRr,
        CancellationToken cancellationToken = default)
    {
        string upperSymbol = symbol.ToUpperInvariant().Trim();

        return await _context.Trades
            .Where(t => t.Symbol == upperSymbol
                     && t.LatestEvaluation != null
                     && t.LatestEvaluation.Score >= minScore
                     && t.LatestEvaluation.Score <= maxScore
                     && t.LatestEvaluation.RiskRewardRatio >= minRr
                     && t.LatestEvaluation.RiskRewardRatio <= maxRr
                     && t.Result != null)
            .Select(t => t.Result!)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(TradePrediction prediction, CancellationToken cancellationToken = default)
    {
        await _context.TradePredictions.AddAsync(prediction, cancellationToken);
    }
}
