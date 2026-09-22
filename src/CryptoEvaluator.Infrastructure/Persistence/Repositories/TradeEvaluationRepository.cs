using CryptoEvaluator.Domain.Entities;
using CryptoEvaluator.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CryptoEvaluator.Infrastructure.Persistence.Repositories;

public class TradeEvaluationRepository : ITradeEvaluationRepository
{
    private readonly AppDbContext _context;

    public TradeEvaluationRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<TradeEvaluation?> GetByTradeIdAsync(Guid tradeId, CancellationToken cancellationToken = default)
    {
        return await _context.TradeEvaluations
            .Where(e => e.TradeId == tradeId)
            .OrderByDescending(e => e.Version)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<int> GetLatestVersionAsync(Guid tradeId, CancellationToken cancellationToken = default)
    {
        return await _context.TradeEvaluations
            .Where(e => e.TradeId == tradeId)
            .Select(e => (int?)e.Version)
            .MaxAsync(cancellationToken) ?? 0;
    }

    public async Task AddAsync(TradeEvaluation evaluation, MarketSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        await _context.TradeEvaluations.AddAsync(evaluation, cancellationToken);
        await _context.MarketSnapshots.AddAsync(snapshot, cancellationToken);
    }
}
