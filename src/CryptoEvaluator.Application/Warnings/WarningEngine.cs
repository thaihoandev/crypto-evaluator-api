using CryptoEvaluator.Domain.Entities;
using CryptoEvaluator.Domain.Enums;
using CryptoEvaluator.Domain.Models;

namespace CryptoEvaluator.Application.Warnings;

public interface IWarningEngine
{
    IReadOnlyList<TradeWarning> GenerateWarnings(
        Trade trade,
        IndicatorSnapshot indicators,
        RiskResult risk);
}

public class WarningEngine : IWarningEngine
{
    public IReadOnlyList<TradeWarning> GenerateWarnings(
        Trade trade,
        IndicatorSnapshot indicators,
        RiskResult risk)
    {
        var warnings = new List<TradeWarning>();

        if (risk.RiskRewardRatio < 1.0m)
        {
            warnings.Add(new TradeWarning(
                Code: "POOR_RISK_REWARD",
                Message: $"Risk/reward ratio is 1:{risk.RiskRewardRatio:F2}, which is below 1:1 ratio.",
                Severity: WarningSeverity.Critical));
        }

        if (trade.RiskPercent > 2.0m)
        {
            warnings.Add(new TradeWarning(
                Code: "HIGH_ACCOUNT_RISK",
                Message: $"Configured risk percent ({trade.RiskPercent:F1}%) exceeds recommended 2% per trade limit.",
                Severity: WarningSeverity.Warning));
        }

        if (risk.MarginRequired > trade.AccountBalance)
        {
            warnings.Add(new TradeWarning(
                Code: "EXCESSIVE_MARGIN_REQUIRED",
                Message: $"Required margin (${risk.MarginRequired:F2}) exceeds total account balance (${trade.AccountBalance:F2}).",
                Severity: WarningSeverity.Critical));
        }

        if (indicators.VolumeRatio < 0.5m)
        {
            warnings.Add(new TradeWarning(
                Code: "LOW_VOLUME",
                Message: $"Current volume ratio is {indicators.VolumeRatio:F2}, significantly below average volume.",
                Severity: WarningSeverity.Warning));
        }

        if (trade.Direction == TradeDirection.Long && indicators.Rsi > 80m)
        {
            warnings.Add(new TradeWarning(
                Code: "RSI_OVERBOUGHT",
                Message: $"RSI is at {indicators.Rsi:F1}, indicating momentum is overheated for a Long entry.",
                Severity: WarningSeverity.Warning));
        }
        else if (trade.Direction == TradeDirection.Short && indicators.Rsi < 30m)
        {
            warnings.Add(new TradeWarning(
                Code: "RSI_OVERSOLD",
                Message: $"RSI is at {indicators.Rsi:F1}, indicating price is oversold for a Short entry.",
                Severity: WarningSeverity.Warning));
        }

        return warnings;
    }
}
