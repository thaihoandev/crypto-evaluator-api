using CryptoEvaluator.Domain.Entities;
using CryptoEvaluator.Domain.Enums;
using CryptoEvaluator.Domain.Interfaces;
using CryptoEvaluator.Domain.Models;

namespace CryptoEvaluator.Application.Prediction;

public interface IHistoricalAnalogPredictor
{
    Task<TradePredictionResult?> PredictAsync(
        Trade trade,
        decimal score,
        RiskResult risk,
        CancellationToken cancellationToken = default);
}

public class HistoricalAnalogPredictor : IHistoricalAnalogPredictor
{
    private readonly ITradePredictionRepository _repository;

    public HistoricalAnalogPredictor(ITradePredictionRepository repository)
    {
        _repository = repository;
    }

    public async Task<TradePredictionResult?> PredictAsync(
        Trade trade,
        decimal score,
        RiskResult risk,
        CancellationToken cancellationToken = default)
    {
        decimal minScore = Math.Max(0m, score - 10m);
        decimal maxScore = Math.Min(100m, score + 10m);
        decimal minRr = Math.Max(0.1m, risk.RiskRewardRatio - 0.5m);
        decimal maxRr = risk.RiskRewardRatio + 0.5m;

        var historicalTrades = await _repository.GetSimilarHistoricalTradesAsync(
            trade.Symbol, minScore, maxScore, minRr, maxRr, cancellationToken);

        if (historicalTrades.Count < 30)
        {
            return null; // Not enough sample size for historical analog
        }

        int winCount = historicalTrades.Count(t => t.IsWin);
        decimal winProb = (decimal)winCount / historicalTrades.Count * 100m;
        decimal lossProb = 100m - winProb;
        decimal avgR = historicalTrades.Average(t => t.RMultiple);

        return new TradePredictionResult(
            WinProbability: Math.Round(winProb, 2),
            LossProbability: Math.Round(lossProb, 2),
            NoHitProbability: 0m,
            ExpectedRMultiple: Math.Round(avgR, 4),
            SimulatedPaths: historicalTrades.Count,
            Method: PredictionMethod.HistoricalAnalog,
            RandomSeed: null,
            SampleSize: historicalTrades.Count);
    }
}
