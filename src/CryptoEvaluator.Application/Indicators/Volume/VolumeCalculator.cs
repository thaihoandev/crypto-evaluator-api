using CryptoEvaluator.Domain.Models;

namespace CryptoEvaluator.Application.Indicators.Volume;

public record VolumeAnalysis(decimal CurrentVolume, decimal AverageVolume, decimal VolumeRatio);

public interface IVolumeCalculator
{
    VolumeAnalysis Calculate(IReadOnlyList<Candle> candles, int period = 20);
}

public class VolumeCalculator : IVolumeCalculator
{
    public VolumeAnalysis Calculate(IReadOnlyList<Candle> candles, int period = 20)
    {
        if (candles == null || candles.Count == 0)
            return new VolumeAnalysis(0m, 0m, 1m);

        decimal currentVolume = candles[^1].Volume;

        int sampleSize = Math.Min(candles.Count, period);
        decimal sumVolume = 0m;

        for (int i = candles.Count - sampleSize; i < candles.Count; i++)
        {
            sumVolume += candles[i].Volume;
        }

        decimal averageVolume = sumVolume / sampleSize;
        decimal volumeRatio = averageVolume > 0m ? currentVolume / averageVolume : 1m;

        return new VolumeAnalysis(
            Math.Round(currentVolume, 4),
            Math.Round(averageVolume, 4),
            Math.Round(volumeRatio, 4));
    }
}
