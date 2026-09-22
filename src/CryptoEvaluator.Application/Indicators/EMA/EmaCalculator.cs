using CryptoEvaluator.Domain.Models;

namespace CryptoEvaluator.Application.Indicators.EMA;

public interface IEmaCalculator
{
    decimal Calculate(IReadOnlyList<Candle> candles, int period);
}

public class EmaCalculator : IEmaCalculator
{
    public decimal Calculate(IReadOnlyList<Candle> candles, int period)
    {
        if (candles == null || candles.Count < period)
            return 0m;

        decimal multiplier = 2m / (period + 1m);
        
        // Initial SMA over the first 'period' candles
        decimal sum = 0m;
        for (int i = 0; i < period; i++)
        {
            sum += candles[i].Close;
        }

        decimal ema = sum / period;

        // Apply EMA formula for subsequent candles
        for (int i = period; i < candles.Count; i++)
        {
            ema = (candles[i].Close * multiplier) + (ema * (1m - multiplier));
        }

        return Math.Round(ema, 4);
    }
}
