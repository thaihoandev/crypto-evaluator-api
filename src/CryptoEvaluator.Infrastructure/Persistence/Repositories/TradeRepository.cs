using CryptoEvaluator.Domain.Entities;
using CryptoEvaluator.Domain.Enums;
using CryptoEvaluator.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CryptoEvaluator.Infrastructure.Persistence.Repositories;

public class TradeRepository : ITradeRepository
{
    private readonly AppDbContext _context;

    public TradeRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Trade?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Trades
            .Include(t => t.LatestEvaluation)
            .Include(t => t.LatestSnapshot)
            .Include(t => t.LatestPrediction)
            .Include(t => t.Result)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Trade>> GetUserTradesAsync(
        Guid userId,
        string? symbol = null,
        TradeDirection? direction = null,
        TradeStatus? status = null,
        decimal? minScore = null,
        decimal? maxScore = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Trades
            .Include(t => t.LatestEvaluation)
            .Include(t => t.Result)
            .Where(t => t.UserId == userId);

        if (!string.IsNullOrWhiteSpace(symbol))
        {
            string upperSymbol = symbol.ToUpperInvariant().Trim();
            query = query.Where(t => t.Symbol == upperSymbol);
        }

        if (direction.HasValue)
        {
            query = query.Where(t => t.Direction == direction.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(t => t.Status == status.Value);
        }

        if (minScore.HasValue)
        {
            query = query.Where(t => t.LatestEvaluation != null && t.LatestEvaluation.Score >= minScore.Value);
        }

        if (maxScore.HasValue)
        {
            query = query.Where(t => t.LatestEvaluation != null && t.LatestEvaluation.Score <= maxScore.Value);
        }

        return await query.OrderByDescending(t => t.CreatedAt).ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Trade trade, CancellationToken cancellationToken = default)
    {
        await _context.Trades.AddAsync(trade, cancellationToken);
    }

    public void Update(Trade trade)
    {
        _context.Trades.Update(trade);
    }
}
