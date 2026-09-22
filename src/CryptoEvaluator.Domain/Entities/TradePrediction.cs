using CryptoEvaluator.Domain.Enums;

namespace CryptoEvaluator.Domain.Entities;

public class TradePrediction
{
    public Guid Id { get; private set; }
    public Guid TradeId { get; private set; }
    public Guid EvaluationId { get; private set; }
    public decimal WinProbability { get; private set; }
    public decimal LossProbability { get; private set; }
    public decimal NoHitProbability { get; private set; }
    public decimal ExpectedRMultiple { get; private set; }
    public PredictionMethod Method { get; private set; }
    public int? SampleSize { get; private set; }
    public int? RandomSeed { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private TradePrediction() { }

    public static TradePrediction CreateMonteCarlo(
        Guid tradeId,
        Guid evaluationId,
        decimal winProbability,
        decimal lossProbability,
        decimal noHitProbability,
        decimal expectedRMultiple,
        int? randomSeed)
    {
        return new TradePrediction
        {
            Id = Guid.NewGuid(),
            TradeId = tradeId,
            EvaluationId = evaluationId,
            WinProbability = Math.Round(winProbability, 2),
            LossProbability = Math.Round(lossProbability, 2),
            NoHitProbability = Math.Round(noHitProbability, 2),
            ExpectedRMultiple = Math.Round(expectedRMultiple, 4),
            Method = PredictionMethod.MonteCarloGbm,
            RandomSeed = randomSeed,
            CreatedAt = DateTime.UtcNow
        };
    }

    public static TradePrediction CreateHistoricalAnalog(
        Guid tradeId,
        Guid evaluationId,
        decimal winProbability,
        decimal lossProbability,
        decimal expectedRMultiple,
        int sampleSize)
    {
        return new TradePrediction
        {
            Id = Guid.NewGuid(),
            TradeId = tradeId,
            EvaluationId = evaluationId,
            WinProbability = Math.Round(winProbability, 2),
            LossProbability = Math.Round(lossProbability, 2),
            NoHitProbability = 0m,
            ExpectedRMultiple = Math.Round(expectedRMultiple, 4),
            Method = PredictionMethod.HistoricalAnalog,
            SampleSize = sampleSize,
            CreatedAt = DateTime.UtcNow
        };
    }
}
