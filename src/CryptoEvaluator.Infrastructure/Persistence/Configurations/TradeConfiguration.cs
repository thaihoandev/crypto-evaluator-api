using CryptoEvaluator.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CryptoEvaluator.Infrastructure.Persistence.Configurations;

public class TradeConfiguration : IEntityTypeConfiguration<Trade>
{
    public void Configure(EntityTypeBuilder<Trade> builder)
    {
        builder.ToTable("trades");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.UserId).IsRequired();
        builder.Property(t => t.Symbol).HasMaxLength(30).IsRequired();
        builder.Property(t => t.Direction).IsRequired();
        builder.Property(t => t.Timeframe).IsRequired();
        builder.Property(t => t.EntryPrice).HasPrecision(30, 10).IsRequired();
        builder.Property(t => t.StopLoss).HasPrecision(30, 10).IsRequired();
        builder.Property(t => t.TakeProfit).HasPrecision(30, 10).IsRequired();
        builder.Property(t => t.AccountBalance).HasPrecision(30, 10).IsRequired();
        builder.Property(t => t.RiskPercent).HasPrecision(10, 4).IsRequired();
        builder.Property(t => t.Leverage).HasPrecision(10, 2).IsRequired();
        builder.Property(t => t.Status).IsRequired();
        builder.Property(t => t.CreatedAt).IsRequired();

        builder.HasOne(t => t.LatestEvaluation)
            .WithOne()
            .HasForeignKey<TradeEvaluation>(e => e.TradeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.LatestSnapshot)
            .WithOne()
            .HasForeignKey<MarketSnapshot>(m => m.TradeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.LatestPrediction)
            .WithOne()
            .HasForeignKey<TradePrediction>(p => p.TradeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.Result)
            .WithOne()
            .HasForeignKey<TradeResult>(r => r.TradeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
