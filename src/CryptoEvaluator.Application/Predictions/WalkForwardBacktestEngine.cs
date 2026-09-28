using CryptoEvaluator.Domain.Entities;
using CryptoEvaluator.Domain.Models;

namespace CryptoEvaluator.Application.Predictions;

public record WalkForwardReportDto(
    string Symbol,
    int TotalWindows,
    decimal WalkForwardAccuracy,
    decimal AverageBrierScore,
    decimal PredictedWinRateAvg,
    decimal ActualWinRateAvg);

public interface IWalkForwardBacktestEngine
{
    /// <summary>
    /// Runs a walk-forward rolling window backtest on historical candles.
    /// Calibration window: trainSize (e.g. 200 candles).
    /// Test window: testHorizon (e.g. 50 candles).
    /// Step size: step (e.g. 10 candles).
    /// </summary>
    WalkForwardReportDto RunBacktest(
        string symbol,
        IReadOnlyList<Candle> historicalCandles,
        int trainSize = 200,
        int testHorizon = 50,
        int step = 10);
}

public class WalkForwardBacktestEngine : IWalkForwardBacktestEngine
{
    public WalkForwardReportDto RunBacktest(
        string symbol,
        IReadOnlyList<Candle> historicalCandles,
        int trainSize = 200,
        int testHorizon = 50,
        int step = 10)
    {
        if (historicalCandles == null || historicalCandles.Count < trainSize + testHorizon)
        {
            return new WalkForwardReportDto(
                Symbol: symbol,
                TotalWindows: 0,
                WalkForwardAccuracy: 0m,
                AverageBrierScore: 0m,
                PredictedWinRateAvg: 0m,
                ActualWinRateAvg: 0m);
        }

        int totalWindows = 0;
        double totalBrier = 0.0;
        int accuratePredictions = 0;
        double sumPredictedWinRate = 0.0;
        double sumActualWin = 0.0;

        for (int i = trainSize; i + testHorizon <= historicalCandles.Count; i += step)
        {
            totalWindows++;

            // Train window [i - trainSize, i)
            var trainCandles = historicalCandles.Skip(i - trainSize).Take(trainSize).ToList();
            // Test window [i, i + testHorizon)
            var testCandles = historicalCandles.Skip(i).Take(testHorizon).ToList();

            double entryPrice = (double)trainCandles[^1].Close;
            double tpPrice = entryPrice * 1.03; // 3% TP default target
            double slPrice = entryPrice * 0.98; // 2% SL default target

            // Compute empirical log-returns on train window
            var logReturns = new List<double>();
            for (int k = 1; k < trainCandles.Count; k++)
            {
                if (trainCandles[k - 1].Close > 0 && trainCandles[k].Close > 0)
                    logReturns.Add(Math.Log((double)(trainCandles[k].Close / trainCandles[k - 1].Close)));
            }

            double meanReturn = logReturns.Count > 0 ? logReturns.Average() : 0.0;
            double stdDev = logReturns.Count > 1
                ? Math.Sqrt(logReturns.Select(r => (r - meanReturn) * (r - meanReturn)).Average())
                : 0.02;

            // Simple Monte Carlo forecast on test window
            double predictedWinRate = Math.Clamp(0.50 + meanReturn / Math.Max(1e-4, stdDev) * 0.1, 0.10, 0.90);
            sumPredictedWinRate += predictedWinRate;

            // Actual outcome on test window
            bool actualWin = false;
            foreach (var candle in testCandles)
            {
                if ((double)candle.High >= tpPrice)
                {
                    actualWin = true;
                    break;
                }
                if ((double)candle.Low <= slPrice)
                {
                    actualWin = false;
                    break;
                }
            }

            double actualVal = actualWin ? 1.0 : 0.0;
            if (actualWin) sumActualWin += 1.0;

            double brier = (predictedWinRate - actualVal) * (predictedWinRate - actualVal);
            totalBrier += brier;

            bool predictedWin = predictedWinRate >= 0.50;
            if (predictedWin == actualWin)
            {
                accuratePredictions++;
            }
        }

        if (totalWindows == 0)
        {
            return new WalkForwardReportDto(symbol, 0, 0m, 0m, 0m, 0m);
        }

        decimal accuracy = Math.Round((decimal)accuratePredictions / totalWindows * 100m, 2);
        decimal avgBrier = Math.Round((decimal)(totalBrier / totalWindows), 4);
        decimal predictedAvg = Math.Round((decimal)(sumPredictedWinRate / totalWindows * 100.0), 2);
        decimal actualAvg = Math.Round((decimal)(sumActualWin / totalWindows * 100.0), 2);

        return new WalkForwardReportDto(
            Symbol: symbol,
            TotalWindows: totalWindows,
            WalkForwardAccuracy: accuracy,
            AverageBrierScore: avgBrier,
            PredictedWinRateAvg: predictedAvg,
            ActualWinRateAvg: actualAvg);
    }
}
