using CryptoEvaluator.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CryptoEvaluator.Infrastructure.Persistence.Configurations;

public class TradeEvaluationConfiguration : IEntityTypeConfiguration<TradeEvaluation>
{
    public void Configure(EntityTypeBuilder<TradeEvaluation> builder)
    {
        builder.ToTable("trade_evaluations");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.TradeId).IsRequired();
        builder.Property(e => e.Version).IsRequired();
        builder.Property(e => e.RiskRewardRatio).HasPrecision(20, 8).IsRequired();
        builder.Property(e => e.RiskAmount).HasPrecision(30, 10).IsRequired();
        builder.Property(e => e.PositionSize).HasPrecision(30, 10).IsRequired();
        builder.Property(e => e.MarginRequired).HasPrecision(30, 10).IsRequired();
        builder.Property(e => e.Score).HasPrecision(5, 2).IsRequired();
        builder.Property(e => e.Explanation).HasColumnType("text");
        builder.Property(e => e.CreatedAt).IsRequired();
    }
}
