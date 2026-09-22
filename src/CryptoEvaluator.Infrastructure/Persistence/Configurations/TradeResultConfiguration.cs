using CryptoEvaluator.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CryptoEvaluator.Infrastructure.Persistence.Configurations;

public class TradeResultConfiguration : IEntityTypeConfiguration<TradeResult>
{
    public void Configure(EntityTypeBuilder<TradeResult> builder)
    {
        builder.ToTable("trade_results");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.TradeId).IsRequired();
        builder.Property(r => r.ExitPrice).HasPrecision(30, 10).IsRequired();
        builder.Property(r => r.Pnl).HasPrecision(30, 10).IsRequired();
        builder.Property(r => r.RMultiple).HasPrecision(10, 4).IsRequired();
        builder.Property(r => r.IsWin).IsRequired();
        builder.Property(r => r.ExitReason).HasMaxLength(200);
        builder.Property(r => r.Notes).HasColumnType("text");
        builder.Property(r => r.ClosedAt).IsRequired();
    }
}
