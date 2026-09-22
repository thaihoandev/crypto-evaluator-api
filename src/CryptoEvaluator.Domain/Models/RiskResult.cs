namespace CryptoEvaluator.Domain.Models;

public record RiskResult(
    decimal RiskAmount,
    decimal StopLossDistance,
    decimal TakeProfitDistance,
    decimal RiskRewardRatio,
    decimal PositionSize,
    decimal MarginRequired);
