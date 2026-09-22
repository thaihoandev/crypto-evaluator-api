namespace CryptoEvaluator.Domain.Entities;

public class MarketSnapshot
{
    public Guid Id { get; private set; }
    public Guid TradeId { get; private set; }
    public decimal CurrentPrice { get; private set; }
    public decimal Ema20 { get; private set; }
    public decimal Ema50 { get; private set; }
    public decimal Ema200 { get; private set; }
    public decimal Rsi { get; private set; }
    public decimal Atr { get; private set; }
    public decimal Volume { get; private set; }
    public decimal AverageVolume { get; private set; }
    public decimal VolumeRatio { get; private set; }
    public DateTime Timestamp { get; private set; }

    private MarketSnapshot() { }

    public static MarketSnapshot Create(
        Guid tradeId,
        decimal currentPrice,
        decimal ema20,
        decimal ema50,
        decimal ema200,
        decimal rsi,
        decimal atr,
        decimal volume,
        decimal averageVolume,
        decimal volumeRatio,
        DateTime timestamp)
    {
        return new MarketSnapshot
        {
            Id = Guid.NewGuid(),
            TradeId = tradeId,
            CurrentPrice = currentPrice,
            Ema20 = ema20,
            Ema50 = ema50,
            Ema200 = ema200,
            Rsi = rsi,
            Atr = atr,
            Volume = volume,
            AverageVolume = averageVolume,
            VolumeRatio = volumeRatio,
            Timestamp = timestamp
        };
    }
}
