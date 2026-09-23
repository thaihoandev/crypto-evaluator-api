using CryptoEvaluator.Domain.Entities;
using CryptoEvaluator.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

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

        // Npgsql maps PostgreSQL "timestamp with time zone" to DateTime values
        // whose Kind must be Utc. Treat offset-less values as UTC because all
        // timestamps in this service represent market/server time, not local time.
        var utcDateTimeConverter = new ValueConverter<DateTime, DateTime>(
            value => ToUtc(value),
            value => ToUtc(value));
        var nullableUtcDateTimeConverter = new ValueConverter<DateTime?, DateTime?>(
            value => value.HasValue ? ToUtc(value.Value) : null,
            value => value.HasValue ? ToUtc(value.Value) : null);

        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(entity => entity.GetProperties()))
        {
            if (property.ClrType == typeof(DateTime))
                property.SetValueConverter(utcDateTimeConverter);
            else if (property.ClrType == typeof(DateTime?))
                property.SetValueConverter(nullableUtcDateTimeConverter);
        }
    }

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
}
