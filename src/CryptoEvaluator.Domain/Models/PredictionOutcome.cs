namespace CryptoEvaluator.Domain.Models;

/// <summary>A resolved prediction paired with the eventual trade result.</summary>
public record PredictionOutcome(
    decimal PredictedWinProbability,
    decimal ExpectedRMultiple,
    bool IsWin,
    decimal RealizedRMultiple,
    DateTime PredictionCreatedAt,
    DateTime ClosedAt);
