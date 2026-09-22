using CryptoEvaluator.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CryptoEvaluator.Infrastructure.Persistence.Configurations;

public class StrategyRuleConfiguration : IEntityTypeConfiguration<StrategyRule>
{
    public void Configure(EntityTypeBuilder<StrategyRule> builder)
    {
        builder.ToTable("strategy_rules");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.StrategyId).IsRequired();
        builder.Property(r => r.Indicator).HasMaxLength(50).IsRequired();
        builder.Property(r => r.Operator).HasMaxLength(10).IsRequired();
        builder.Property(r => r.Value).HasPrecision(30, 10).IsRequired();
        builder.Property(r => r.Weight).IsRequired();
    }
}
