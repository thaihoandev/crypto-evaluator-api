using CryptoEvaluator.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CryptoEvaluator.Infrastructure.Persistence.Configurations;

public class MarketSnapshotConfiguration : IEntityTypeConfiguration<MarketSnapshot>
{
    public void Configure(EntityTypeBuilder<MarketSnapshot> builder)
    {
        builder.ToTable("market_snapshots");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.TradeId).IsRequired();
        builder.Property(m => m.CurrentPrice).HasPrecision(30, 10).IsRequired();
        builder.Property(m => m.Ema20).HasPrecision(30, 10);
        builder.Property(m => m.Ema50).HasPrecision(30, 10);
        builder.Property(m => m.Ema200).HasPrecision(30, 10);
        builder.Property(m => m.Rsi).HasPrecision(10, 4);
        builder.Property(m => m.Atr).HasPrecision(30, 10);
        builder.Property(m => m.Volume).HasPrecision(30, 10);
        builder.Property(m => m.AverageVolume).HasPrecision(30, 10);
        builder.Property(m => m.VolumeRatio).HasPrecision(20, 8);
        builder.Property(m => m.Timestamp).IsRequired();
    }
}
