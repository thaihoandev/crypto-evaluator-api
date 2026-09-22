namespace CryptoEvaluator.Domain.Entities;

public class TradeResult
{
    public Guid Id { get; private set; }
    public Guid TradeId { get; private set; }
    public decimal ExitPrice { get; private set; }
    public decimal Pnl { get; private set; }
    public decimal RMultiple { get; private set; }
    public bool IsWin { get; private set; }
    public string? ExitReason { get; private set; }
    public string? Notes { get; private set; }
    public DateTime ClosedAt { get; private set; }

    private TradeResult() { }

    public static TradeResult Create(
        Guid tradeId,
        decimal exitPrice,
        decimal pnl,
        decimal rMultiple,
        bool isWin,
        string? exitReason,
        string? notes)
    {
        return new TradeResult
        {
            Id = Guid.NewGuid(),
            TradeId = tradeId,
            ExitPrice = exitPrice,
            Pnl = Math.Round(pnl, 4),
            RMultiple = Math.Round(rMultiple, 4),
            IsWin = isWin,
            ExitReason = exitReason,
            Notes = notes,
            ClosedAt = DateTime.UtcNow
        };
    }
}
