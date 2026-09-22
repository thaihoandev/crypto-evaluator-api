using CryptoEvaluator.Application.Prediction;
using CryptoEvaluator.Domain.Entities;
using CryptoEvaluator.Domain.Enums;
using CryptoEvaluator.Domain.Models;
using FluentAssertions;
using Xunit;

namespace CryptoEvaluator.Application.Tests;

public class MonteCarloGbmPredictorTests
{
    private readonly MonteCarloGbmPredictor _predictor = new();

    [Fact]
    public void Predict_WithFixedRandomSeed_ShouldBeReproducible()
    {
        // Arrange
        var trade = Trade.Create(
            userId: Guid.NewGuid(),
            symbol: "BTCUSDT",
            direction: TradeDirection.Long,
            timeframe: Timeframe.H1,
            entryPrice: 104500m,
            stopLoss: 102500m,
            takeProfit: 108500m,
            accountBalance: 10000m,
            riskPercent: 1.0m,
            leverage: 5m);

        var indicators = new IndicatorSnapshot(
            CurrentPrice: 104500m,
            Ema20: 103800m,
            Ema50: 102900m,
            Ema200: 98000m,
            Rsi: 61m,
            Atr: 1800m,
            Volume: 1000m,
            AverageVolume: 900m,
            VolumeRatio: 1.11m,
            Timestamp: DateTime.UtcNow);

        var candles = new List<Candle>
        {
            new(DateTime.UtcNow.AddHours(-2), 104000m, 104600m, 103900m, 104500m, 1000m),
            new(DateTime.UtcNow.AddHours(-1), 104500m, 104800m, 104200m, 104500m, 1050m)
        };

        const int fixedSeed = 42;

        DateTime lastCandleTime = candles[^1].OpenTime;

        // Act
        var result1 = _predictor.Predict(trade, indicators, candles, lastCandleTime, randomSeed: fixedSeed, numPaths: 2000);
        var result2 = _predictor.Predict(trade, indicators, candles, lastCandleTime, randomSeed: fixedSeed, numPaths: 2000);

        // Assert
        result1.WinProbability.Should().Be(result2.WinProbability);
        result1.LossProbability.Should().Be(result2.LossProbability);
        result1.NoHitProbability.Should().Be(result2.NoHitProbability);
        result1.ExpectedRMultiple.Should().Be(result2.ExpectedRMultiple);
        (result1.WinProbability + result1.LossProbability + result1.NoHitProbability).Should().Be(100.0m);
        result1.ScenarioPaths.Should().NotBeNull();
        result1.ScenarioPaths!.Select(path => path.Name).Should().Equal("Bear", "Base", "Bull");
        result1.ScenarioPaths.Should().OnlyContain(path => path.Points.Count == 26);

        // Scenario lines must be coherent simulations and therefore separate at
        // the horizon; percentile bounds alone are not rendered as candles.
        var terminalPrices = result1.ScenarioPaths.Select(path => path.Points[^1].Price).ToList();
        terminalPrices.Distinct().Should().HaveCountGreaterThan(1);
        result1.ScenarioPaths[0].Points[0].Price.Should().Be(candles[^1].Close);
    }
}
