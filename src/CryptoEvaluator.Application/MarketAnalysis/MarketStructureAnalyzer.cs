using CryptoEvaluator.Domain.Models;

namespace CryptoEvaluator.Application.MarketAnalysis;

public record MarketStructureSnapshot(decimal? Support, decimal? Resistance);

public interface IMarketStructureAnalyzer
{
    MarketStructureSnapshot Analyze(
        IReadOnlyList<Candle> candles,
        decimal currentPrice,
        int lookbackCandles,
        int pivotStrength);
}

public class MarketStructureAnalyzer : IMarketStructureAnalyzer
{
    public MarketStructureSnapshot Analyze(
        IReadOnlyList<Candle> candles,
        decimal currentPrice,
        int lookbackCandles,
        int pivotStrength)
    {
        ArgumentNullException.ThrowIfNull(candles);
        if (pivotStrength < 1) throw new ArgumentOutOfRangeException(nameof(pivotStrength));

        int start = Math.Max(pivotStrength, candles.Count - Math.Max(lookbackCandles, pivotStrength * 2 + 1));
        int endExclusive = candles.Count - pivotStrength;
        var swingLows = new List<decimal>();
        var swingHighs = new List<decimal>();

        // A pivot is considered only after enough subsequent closed candles have
        // confirmed it, avoiding levels that repaint while a candle is still open.
        for (int i = start; i < endExclusive; i++)
        {
            bool isLow = true;
            bool isHigh = true;
            for (int offset = 1; offset <= pivotStrength; offset++)
            {
                if (candles[i].Low > candles[i - offset].Low || candles[i].Low > candles[i + offset].Low)
                    isLow = false;
                if (candles[i].High < candles[i - offset].High || candles[i].High < candles[i + offset].High)
                    isHigh = false;
            }
            if (isLow) swingLows.Add(candles[i].Low);
            if (isHigh) swingHighs.Add(candles[i].High);
        }

        decimal? support = swingLows.Where(x => x < currentPrice).DefaultIfEmpty().Max();
        decimal? resistance = swingHighs.Where(x => x > currentPrice).DefaultIfEmpty().Min();
        return new MarketStructureSnapshot(
            support is > 0m ? support : null,
            resistance is > 0m ? resistance : null);
    }
}
