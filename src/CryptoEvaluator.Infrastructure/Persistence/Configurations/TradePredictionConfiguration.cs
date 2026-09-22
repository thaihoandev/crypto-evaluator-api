using CryptoEvaluator.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CryptoEvaluator.Infrastructure.Persistence.Configurations;

public class TradePredictionConfiguration : IEntityTypeConfiguration<TradePrediction>
{
    public void Configure(EntityTypeBuilder<TradePrediction> builder)
    {
        builder.ToTable("trade_predictions");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.TradeId).IsRequired();
        builder.Property(p => p.EvaluationId).IsRequired();
        builder.Property(p => p.WinProbability).HasPrecision(5, 2).IsRequired();
        builder.Property(p => p.LossProbability).HasPrecision(5, 2).IsRequired();
        builder.Property(p => p.NoHitProbability).HasPrecision(5, 2).IsRequired();
        builder.Property(p => p.ExpectedRMultiple).HasPrecision(10, 4).IsRequired();
        builder.Property(p => p.Method).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(p => p.SampleSize);
        builder.Property(p => p.RandomSeed);
        builder.Property(p => p.CreatedAt).IsRequired();
    }
}
