namespace CryptoEvaluator.Domain.Entities;

public class StrategyRule
{
    public Guid Id { get; private set; }
    public Guid StrategyId { get; private set; }
    public string Indicator { get; private set; } = null!;
    public string Operator { get; private set; } = null!;
    public decimal Value { get; private set; }
    public int Weight { get; private set; }

    private StrategyRule() { }

    public static StrategyRule Create(Guid strategyId, string indicator, string op, decimal value, int weight)
    {
        return new StrategyRule
        {
            Id = Guid.NewGuid(),
            StrategyId = strategyId,
            Indicator = indicator,
            Operator = op,
            Value = value,
            Weight = weight
        };
    }
}
