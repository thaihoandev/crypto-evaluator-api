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

    /// <summary>
    /// §7 Time-Weighted k-NN: half-life of ~6 months (δ = ln(2) / 180).
    /// Trades closed 180 days ago receive half the weight of a trade closed today.
    /// Configurable range: 90–365 days depending on data density.
    /// </summary>
    private const double DecayHalfLifeDays = 180.0;
    private static readonly double DecayRate = Math.Log(2.0) / DecayHalfLifeDays;

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

        DateTime now = DateTime.UtcNow;

        // ── §7 Extension: Time-Weighted k-NN ─────────────────────────────────
        // Weight each analog sample by w_i = e^{-δ · Age_i}
        // where Age_i = days since trade closed.
        double totalWeight    = 0.0;
        double weightedWins   = 0.0;
        double weightedR      = 0.0;

        foreach (var t in historicalTrades)
        {
            double ageDays = Math.Max(0.0, (now - t.ClosedAt).TotalDays);
            double weight  = Math.Exp(-DecayRate * ageDays);

            totalWeight  += weight;
            weightedR    += weight * (double)t.RMultiple;
            if (t.IsWin) weightedWins += weight;
        }

        // Guard: if all samples have negligible weight (e.g. all very old), fall back to uniform
        if (totalWeight < 1e-9)
        {
            totalWeight  = historicalTrades.Count;
            weightedWins = historicalTrades.Count(t => t.IsWin);
            weightedR    = (double)historicalTrades.Average(t => t.RMultiple);
        }

        decimal winProb  = Math.Round((decimal)(weightedWins / totalWeight * 100.0), 2);
        decimal lossProb = Math.Round(100m - winProb, 2);
        decimal avgR     = Math.Round((decimal)(weightedR / totalWeight), 4);

        return new TradePredictionResult(
            WinProbability:   winProb,
            LossProbability:  lossProb,
            NoHitProbability: 0m,
            ExpectedRMultiple: avgR,
            SimulatedPaths:   historicalTrades.Count,
            Method:           PredictionMethod.HistoricalAnalog,
            RandomSeed:       null,
            SampleSize:       historicalTrades.Count);
    }
}
