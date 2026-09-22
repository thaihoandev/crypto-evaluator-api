using CryptoEvaluator.Domain.Models;

namespace CryptoEvaluator.Application.Evaluations.Models;

public record CandleDto(
    DateTime OpenTime,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume);

public record EvaluateTradeResponse(
    Guid TradeId,
    Guid EvaluationId,
    int Version,
    decimal Score,
    decimal RiskRewardRatio,
    decimal RiskAmount,
    decimal PositionSize,
    decimal MarginRequired,
    IReadOnlyList<ScoreComponent> Components,
    MarketSnapshotDto Snapshot,
    TradePredictionDto Prediction,
    IReadOnlyList<TradeWarning> Warnings,
    string Explanation,
    IReadOnlyList<CandleDto>? Candles = null);

public record MarketSnapshotDto(
    decimal CurrentPrice,
    decimal Ema20,
    decimal Ema50,
    decimal Ema200,
    decimal Rsi,
    decimal Atr,
    decimal VolumeRatio);

public record TrajectoryPointDto(
    int Step,
    decimal Price,
    decimal UpperBound,
    decimal LowerBound,
    DateTime Timestamp,
    decimal ExpectedOpen = 0m,
    decimal ExpectedHigh = 0m,
    decimal ExpectedLow = 0m,
    decimal ExpectedClose = 0m,
    decimal CumulativeTpHitProb = 0m,
    decimal CumulativeSlHitProb = 0m);

public record TradePredictionDto(
    decimal WinProbability,
    decimal LossProbability,
    decimal NoHitProbability,
    decimal ExpectedRMultiple,
    string Method,
    int? SimulatedPaths,
    int? SampleSize,
    int? RandomSeed,
    string Disclaimer,
    IReadOnlyList<TrajectoryPointDto>? TrajectoryPoints = null);
