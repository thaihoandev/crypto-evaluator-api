namespace CryptoEvaluator.Domain.Entities;

public class TradeEvaluation
{
    public Guid Id { get; private set; }
    public Guid TradeId { get; private set; }
    public int Version { get; private set; }
    public decimal RiskRewardRatio { get; private set; }
    public decimal RiskAmount { get; private set; }
    public decimal PositionSize { get; private set; }
    public decimal MarginRequired { get; private set; }
    public decimal Score { get; private set; }
    public string? Explanation { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private TradeEvaluation() { }

    public static TradeEvaluation Create(
        Guid tradeId,
        int version,
        decimal riskRewardRatio,
        decimal riskAmount,
        decimal positionSize,
        decimal marginRequired,
        decimal score,
        string? explanation)
    {
        return new TradeEvaluation
        {
            Id = Guid.NewGuid(),
            TradeId = tradeId,
            Version = version,
            RiskRewardRatio = Math.Round(riskRewardRatio, 4),
            RiskAmount = Math.Round(riskAmount, 4),
            PositionSize = Math.Round(positionSize, 6),
            MarginRequired = Math.Round(marginRequired, 4),
            Score = Math.Round(score, 2),
            Explanation = explanation,
            CreatedAt = DateTime.UtcNow
        };
    }
}
