namespace CryptoEvaluator.Domain.Models;

public record Candle(
    DateTime OpenTime,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume,
    DateTime? CloseTime = null,
    decimal? QuoteVolume = null);
