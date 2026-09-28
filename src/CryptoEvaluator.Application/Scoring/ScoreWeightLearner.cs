using CryptoEvaluator.Domain.Entities;
using CryptoEvaluator.Domain.Interfaces;

namespace CryptoEvaluator.Application.Scoring;

public interface IScoreWeightLearner
{
    /// <summary>
    /// Fits logistic regression weights (w1..w8) on historical trade outcomes.
    /// Returns normalised weights summing to 1.0 (100%).
    /// </summary>
    Task<Dictionary<string, double>> LearnWeightsAsync(
        IReadOnlyList<Trade> historicalTrades,
        CancellationToken cancellationToken = default);
}

public class ScoreWeightLearner : IScoreWeightLearner
{
    private const int MinSampleSize = 200;
    private const double LearningRate = 0.01;
    private const int Epochs = 500;

    public Task<Dictionary<string, double>> LearnWeightsAsync(
        IReadOnlyList<Trade> historicalTrades,
        CancellationToken cancellationToken = default)
    {
        // Default weights if sample size is insufficient
        var defaultWeights = new Dictionary<string, double>
        {
            ["TrendAlignmentRule"]     = 0.20,
            ["EntryQualityRule"]       = 0.15,
            ["MomentumRule"]           = 0.15,
            ["VolumeConfirmationRule"] = 0.10,
            ["SupportResistanceRule"]  = 0.10,
            ["RiskRewardRule"]         = 0.15,
            ["VolatilityFitRule"]      = 0.15,
            ["MarketContextRule"]      = 0.05
        };

        if (historicalTrades == null || historicalTrades.Count < MinSampleSize)
        {
            return Task.FromResult(defaultWeights);
        }

        // Feature names in deterministic order
        string[] ruleNames = [
            "TrendAlignmentRule",
            "EntryQualityRule",
            "MomentumRule",
            "VolumeConfirmationRule",
            "SupportResistanceRule",
            "RiskRewardRule",
            "VolatilityFitRule",
            "MarketContextRule"
        ];

        int n = historicalTrades.Count;
        int numFeatures = ruleNames.Length;
        double[] weights = new double[numFeatures];
        for (int j = 0; j < numFeatures; j++) weights[j] = defaultWeights[ruleNames[j]];
        double bias = 0.0;

        // Gradient descent for logistic regression
        for (int epoch = 0; epoch < Epochs; epoch++)
        {
            double[] dw = new double[numFeatures];
            double db = 0.0;

            for (int i = 0; i < n; i++)
            {
                var trade = historicalTrades[i];
                double target = (trade.Result != null && trade.Result.IsWin) ? 1.0 : 0.0;

                // Feature vector (scaled 0..1)
                double scoreVal = (double)(trade.LatestEvaluation?.Score ?? 50m);
                double scoreNormalized = scoreVal / 100.0;
                double z = bias;
                for (int j = 0; j < numFeatures; j++) z += weights[j] * scoreNormalized;

                double pred = 1.0 / (1.0 + Math.Exp(-Math.Clamp(z, -10.0, 10.0)));
                double err = pred - target;

                db += err;
                for (int j = 0; j < numFeatures; j++) dw[j] += err * scoreNormalized;
            }

            bias -= LearningRate * (db / n);
            for (int j = 0; j < numFeatures; j++)
                weights[j] -= LearningRate * (dw[j] / n);
        }

        // Softmax / Non-negative normalisation
        double sumPos = 0.0;
        for (int j = 0; j < numFeatures; j++)
        {
            weights[j] = Math.Max(0.01, weights[j]);
            sumPos += weights[j];
        }

        var result = new Dictionary<string, double>();
        for (int j = 0; j < numFeatures; j++)
        {
            result[ruleNames[j]] = Math.Round(weights[j] / sumPos, 4);
        }

        return Task.FromResult(result);
    }
}
