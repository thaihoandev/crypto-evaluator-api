namespace CryptoEvaluator.Domain.Models;

public record MarketTicker(
    string Symbol,
    decimal Price,
    DateTime Timestamp);
