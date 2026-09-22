using CryptoEvaluator.Domain.Entities;
using CryptoEvaluator.Domain.Interfaces;
using CryptoEvaluator.Domain.Models;
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

    public async Task<IReadOnlyList<PredictionOutcome>> GetResolvedOutcomesAsync(
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken = default)
    {
        // Use the latest prediction made before the trade was closed. This avoids
        // scoring a later re-evaluation against an outcome it could already know.
        var query =
            from prediction in _context.TradePredictions
            join result in _context.TradeResults on prediction.TradeId equals result.TradeId
            where prediction.CreatedAt <= result.ClosedAt
                  && !_context.TradePredictions.Any(newer =>
                      newer.TradeId == prediction.TradeId
                      && newer.CreatedAt > prediction.CreatedAt
                      && newer.CreatedAt <= result.ClosedAt)
            select new { prediction, result };

        if (from.HasValue)
            query = query.Where(x => x.result.ClosedAt >= from.Value);
        if (to.HasValue)
            query = query.Where(x => x.result.ClosedAt <= to.Value);

        return await query
            .OrderBy(x => x.result.ClosedAt)
            .Select(x => new PredictionOutcome(
                x.prediction.WinProbability,
                x.prediction.ExpectedRMultiple,
                x.result.IsWin,
                x.result.RMultiple,
                x.prediction.CreatedAt,
                x.result.ClosedAt))
            .ToListAsync(cancellationToken);
    }
}
