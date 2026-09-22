using CryptoEvaluator.Domain.Models;

namespace CryptoEvaluator.Application.Indicators.ATR;

public interface IAtrCalculator
{
    decimal Calculate(IReadOnlyList<Candle> candles, int period = 14);
}

public class AtrCalculator : IAtrCalculator
{
    public decimal Calculate(IReadOnlyList<Candle> candles, int period = 14)
    {
        if (candles == null || candles.Count <= period)
            return 0m;

        decimal trSum = 0m;
        for (int i = 1; i <= period; i++)
        {
            trSum += CalculateTrueRange(candles[i], candles[i - 1].Close);
        }

        decimal atr = trSum / period;

        for (int i = period + 1; i < candles.Count; i++)
        {
            decimal tr = CalculateTrueRange(candles[i], candles[i - 1].Close);
            atr = ((atr * (period - 1)) + tr) / period;
        }

        return Math.Round(atr, 4);
    }

    private static decimal CalculateTrueRange(Candle current, decimal prevClose)
    {
        decimal highLow = current.High - current.Low;
        decimal highPrevClose = Math.Abs(current.High - prevClose);
        decimal lowPrevClose = Math.Abs(current.Low - prevClose);

        return Math.Max(highLow, Math.Max(highPrevClose, lowPrevClose));
    }
}
