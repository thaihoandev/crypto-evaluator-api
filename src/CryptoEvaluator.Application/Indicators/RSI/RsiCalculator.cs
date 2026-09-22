using CryptoEvaluator.Domain.Models;

namespace CryptoEvaluator.Application.Indicators.RSI;

public interface IRsiCalculator
{
    decimal Calculate(IReadOnlyList<Candle> candles, int period = 14);
}

public class RsiCalculator : IRsiCalculator
{
    public decimal Calculate(IReadOnlyList<Candle> candles, int period = 14)
    {
        if (candles == null || candles.Count <= period)
            return 50m; // Neutral fallback

        decimal gains = 0m;
        decimal losses = 0m;

        for (int i = 1; i <= period; i++)
        {
            decimal diff = candles[i].Close - candles[i - 1].Close;
            if (diff >= 0)
                gains += diff;
            else
                losses += Math.Abs(diff);
        }

        decimal avgGain = gains / period;
        decimal avgLoss = losses / period;

        for (int i = period + 1; i < candles.Count; i++)
        {
            decimal diff = candles[i].Close - candles[i - 1].Close;
            decimal currentGain = diff >= 0 ? diff : 0m;
            decimal currentLoss = diff < 0 ? Math.Abs(diff) : 0m;

            avgGain = ((avgGain * (period - 1)) + currentGain) / period;
            avgLoss = ((avgLoss * (period - 1)) + currentLoss) / period;
        }

        if (avgLoss == 0m)
            return 100m;

        decimal rs = avgGain / avgLoss;
        decimal rsi = 100m - (100m / (1m + rs));

        return Math.Round(rsi, 2);
    }
}
