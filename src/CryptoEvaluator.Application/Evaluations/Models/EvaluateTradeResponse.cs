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

// A coherent simulated path, intended for rendering a scenario line or candles.
// Unlike the percentile envelope above, every point belongs to the same simulation.
public record ScenarioPathDto(
    string Name,
    IReadOnlyList<TrajectoryPointDto> Points);

public record PredictionDataQualityDto(
    bool IsSufficient,
    int ClosedCandleCount,
    DateTime LastClosedCandleTime,
    int DataAgeSeconds,
    bool IsStale,
    string Source);

public record PredictionConfidenceDto(
    decimal Score,
    string Level,
    IReadOnlyList<string> Factors);

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
    IReadOnlyList<TrajectoryPointDto>? TrajectoryPoints = null,
    IReadOnlyList<ScenarioPathDto>? ScenarioPaths = null,
    PredictionDataQualityDto? DataQuality = null,
    PredictionConfidenceDto? Confidence = null);
