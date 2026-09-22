using CryptoEvaluator.Domain.Entities;
using CryptoEvaluator.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CryptoEvaluator.Infrastructure.Persistence;

public class AppDbContext : DbContext, IUnitOfWork
{
    public DbSet<Trade> Trades => Set<Trade>();
    public DbSet<TradeEvaluation> TradeEvaluations => Set<TradeEvaluation>();
    public DbSet<MarketSnapshot> MarketSnapshots => Set<MarketSnapshot>();
    public DbSet<TradePrediction> TradePredictions => Set<TradePrediction>();
    public DbSet<TradeResult> TradeResults => Set<TradeResult>();
    public DbSet<Strategy> Strategies => Set<Strategy>();
    public DbSet<StrategyRule> StrategyRules => Set<StrategyRule>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
