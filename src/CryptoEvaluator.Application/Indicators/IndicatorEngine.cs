using CryptoEvaluator.Application.Indicators.ATR;
using CryptoEvaluator.Application.Indicators.EMA;
using CryptoEvaluator.Application.Indicators.RSI;
using CryptoEvaluator.Application.Indicators.Volume;
using CryptoEvaluator.Domain.Models;

namespace CryptoEvaluator.Application.Indicators;

public interface IIndicatorEngine
{
    IndicatorSnapshot CalculateAll(IReadOnlyList<Candle> candles);
}

public class IndicatorEngine : IIndicatorEngine
{
    private readonly IEmaCalculator _emaCalculator;
    private readonly IRsiCalculator _rsiCalculator;
    private readonly IAtrCalculator _atrCalculator;
    private readonly IVolumeCalculator _volumeCalculator;

    public IndicatorEngine(
        IEmaCalculator emaCalculator,
        IRsiCalculator rsiCalculator,
        IAtrCalculator atrCalculator,
        IVolumeCalculator volumeCalculator)
    {
        _emaCalculator = emaCalculator;
        _rsiCalculator = rsiCalculator;
        _atrCalculator = atrCalculator;
        _volumeCalculator = volumeCalculator;
    }

    public IndicatorSnapshot CalculateAll(IReadOnlyList<Candle> candles)
    {
        if (candles == null || candles.Count == 0)
            throw new ArgumentException("Candles list cannot be null or empty.", nameof(candles));

        var latestCandle = candles[^1];
        decimal currentPrice = latestCandle.Close;

        decimal ema20 = _emaCalculator.Calculate(candles, 20);
        decimal ema50 = _emaCalculator.Calculate(candles, 50);
        decimal ema200 = _emaCalculator.Calculate(candles, 200);

        decimal rsi = _rsiCalculator.Calculate(candles, 14);
        decimal atr = _atrCalculator.Calculate(candles, 14);

        var volumeInfo = _volumeCalculator.Calculate(candles, 20);

        return new IndicatorSnapshot(
            CurrentPrice: currentPrice,
            Ema20: ema20,
            Ema50: ema50,
            Ema200: ema200,
            Rsi: rsi,
            Atr: atr,
            Volume: volumeInfo.CurrentVolume,
            AverageVolume: volumeInfo.AverageVolume,
            VolumeRatio: volumeInfo.VolumeRatio,
            Timestamp: latestCandle.OpenTime);
    }
}
