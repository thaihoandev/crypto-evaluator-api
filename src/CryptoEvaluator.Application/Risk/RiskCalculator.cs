using CryptoEvaluator.Domain.Entities;
using CryptoEvaluator.Domain.Enums;
using CryptoEvaluator.Domain.Models;

namespace CryptoEvaluator.Application.Risk;

public interface IRiskCalculator
{
    RiskResult Calculate(Trade trade);
}

public class RiskCalculator : IRiskCalculator
{
    public RiskResult Calculate(Trade trade)
    {
        if (trade == null)
            throw new ArgumentNullException(nameof(trade));

        decimal riskAmount = trade.AccountBalance * (trade.RiskPercent / 100m);

        decimal slDistance = trade.Direction == TradeDirection.Long
            ? trade.EntryPrice - trade.StopLoss
            : trade.StopLoss - trade.EntryPrice;

        if (slDistance <= 0)
            throw new InvalidOperationException("Invalid Stop Loss distance. Stop Loss must be on the loss side of Entry Price.");

        decimal tpDistance = trade.Direction == TradeDirection.Long
            ? trade.TakeProfit - trade.EntryPrice
            : trade.EntryPrice - trade.TakeProfit;

        if (tpDistance <= 0)
            throw new InvalidOperationException("Invalid Take Profit distance. Take Profit must be on the profit side of Entry Price.");

        decimal rrRatio = tpDistance / slDistance;
        decimal positionSize = riskAmount / slDistance;
        decimal notionalValue = positionSize * trade.EntryPrice;
        decimal marginRequired = notionalValue / trade.Leverage;

        return new RiskResult(
            RiskAmount: Math.Round(riskAmount, 4),
            StopLossDistance: Math.Round(slDistance, 4),
            TakeProfitDistance: Math.Round(tpDistance, 4),
            RiskRewardRatio: Math.Round(rrRatio, 4),
            PositionSize: Math.Round(positionSize, 6),
            MarginRequired: Math.Round(marginRequired, 4));
    }
}
