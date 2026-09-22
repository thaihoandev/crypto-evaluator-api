using CryptoEvaluator.Domain.Enums;

namespace CryptoEvaluator.Domain.Entities;

public class Trade
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Symbol { get; private set; } = null!;
    public TradeDirection Direction { get; private set; }
    public Timeframe Timeframe { get; private set; }
    public decimal EntryPrice { get; private set; }
    public decimal StopLoss { get; private set; }
    public decimal TakeProfit { get; private set; }
    public decimal AccountBalance { get; private set; }
    public decimal RiskPercent { get; private set; }
    public decimal Leverage { get; private set; }
    public TradeStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? ClosedAt { get; private set; }

    public TradeEvaluation? LatestEvaluation { get; private set; }
    public MarketSnapshot? LatestSnapshot { get; private set; }
    public TradePrediction? LatestPrediction { get; private set; }
    public TradeResult? Result { get; private set; }

    private Trade() { }

    public static Trade Create(
        Guid userId,
        string symbol,
        TradeDirection direction,
        Timeframe timeframe,
        decimal entryPrice,
        decimal stopLoss,
        decimal takeProfit,
        decimal accountBalance,
        decimal riskPercent,
        decimal leverage)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Symbol cannot be empty.", nameof(symbol));
        if (entryPrice <= 0)
            throw new ArgumentException("Entry price must be positive.", nameof(entryPrice));
        if (stopLoss <= 0)
            throw new ArgumentException("Stop loss must be positive.", nameof(stopLoss));
        if (takeProfit <= 0)
            throw new ArgumentException("Take profit must be positive.", nameof(takeProfit));
        if (accountBalance <= 0)
            throw new ArgumentException("Account balance must be positive.", nameof(accountBalance));
        if (riskPercent <= 0 || riskPercent > 100)
            throw new ArgumentException("Risk percent must be between 0 and 100.", nameof(riskPercent));
        if (leverage <= 0)
            throw new ArgumentException("Leverage must be positive.", nameof(leverage));

        if (direction == TradeDirection.Long)
        {
            if (stopLoss >= entryPrice)
                throw new ArgumentException("For Long trades, Stop Loss must be below Entry Price.", nameof(stopLoss));
            if (takeProfit <= entryPrice)
                throw new ArgumentException("For Long trades, Take Profit must be above Entry Price.", nameof(takeProfit));
        }
        else if (direction == TradeDirection.Short)
        {
            if (stopLoss <= entryPrice)
                throw new ArgumentException("For Short trades, Stop Loss must be above Entry Price.", nameof(stopLoss));
            if (takeProfit >= entryPrice)
                throw new ArgumentException("For Short trades, Take Profit must be below Entry Price.", nameof(takeProfit));
        }

        return new Trade
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Symbol = symbol.ToUpperInvariant().Trim(),
            Direction = direction,
            Timeframe = timeframe,
            EntryPrice = entryPrice,
            StopLoss = stopLoss,
            TakeProfit = takeProfit,
            AccountBalance = accountBalance,
            RiskPercent = riskPercent,
            Leverage = leverage,
            Status = TradeStatus.Planned,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void Close(TradeResult result)
    {
        Result = result ?? throw new ArgumentNullException(nameof(result));
        Status = TradeStatus.Closed;
        ClosedAt = DateTime.UtcNow;
    }

    public void Cancel()
    {
        Status = TradeStatus.Cancelled;
        ClosedAt = DateTime.UtcNow;
    }
}
