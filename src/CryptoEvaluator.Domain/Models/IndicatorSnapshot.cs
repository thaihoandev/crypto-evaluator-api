namespace CryptoEvaluator.Domain.Models;

public record IndicatorSnapshot(
    decimal CurrentPrice,
    decimal Ema20,
    decimal Ema50,
    decimal Ema200,
    decimal Rsi,
    decimal Atr,
    decimal Volume,
    decimal AverageVolume,
    decimal VolumeRatio,
    DateTime Timestamp);
