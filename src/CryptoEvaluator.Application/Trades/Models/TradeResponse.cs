using CryptoEvaluator.Domain.Entities;
using CryptoEvaluator.Domain.Enums;

namespace CryptoEvaluator.Application.Trades.Models;

public record TradeResponse(
    Guid Id,
    Guid UserId,
    string Symbol,
    TradeDirection Direction,
    Timeframe Timeframe,
    decimal EntryPrice,
    decimal StopLoss,
    decimal TakeProfit,
    decimal AccountBalance,
    decimal RiskPercent,
    decimal Leverage,
    TradeStatus Status,
    DateTime CreatedAt,
    DateTime? ClosedAt,
    decimal? LatestScore = null)
{
    public static TradeResponse FromDomain(Trade trade)
    {
        return new TradeResponse(
            Id: trade.Id,
            UserId: trade.UserId,
            Symbol: trade.Symbol,
            Direction: trade.Direction,
            Timeframe: trade.Timeframe,
            EntryPrice: trade.EntryPrice,
            StopLoss: trade.StopLoss,
            TakeProfit: trade.TakeProfit,
            AccountBalance: trade.AccountBalance,
            RiskPercent: trade.RiskPercent,
            Leverage: trade.Leverage,
            Status: trade.Status,
            CreatedAt: trade.CreatedAt,
            ClosedAt: trade.ClosedAt,
            LatestScore: trade.LatestEvaluation?.Score);
    }
}
