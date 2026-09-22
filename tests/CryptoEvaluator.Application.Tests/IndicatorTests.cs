using CryptoEvaluator.Application.Indicators.ATR;
using CryptoEvaluator.Application.Indicators.EMA;
using CryptoEvaluator.Application.Indicators.RSI;
using CryptoEvaluator.Application.Indicators.Volume;
using CryptoEvaluator.Domain.Models;
using FluentAssertions;
using Xunit;

namespace CryptoEvaluator.Application.Tests;

public class IndicatorTests
{
    [Fact]
    public void EmaCalculator_WithKnownDataset_ShouldComputeCorrectEMA()
    {
        var calculator = new EmaCalculator();
        var candles = CreateSampleCandles(30, 100m, 1m);

        var ema20 = calculator.Calculate(candles, 20);

        ema20.Should().BeGreaterThan(100m);
    }

    [Fact]
    public void RsiCalculator_WithUpwardTrend_ShouldReturnHighRSI()
    {
        var calculator = new RsiCalculator();
        var candles = CreateSampleCandles(30, 100m, 2m);

        var rsi = calculator.Calculate(candles, 14);

        rsi.Should().BeGreaterThan(60m);
    }

    [Fact]
    public void AtrCalculator_WithSampleCandles_ShouldReturnPositiveATR()
    {
        var calculator = new AtrCalculator();
        var candles = CreateSampleCandles(30, 100m, 1m);

        var atr = calculator.Calculate(candles, 14);

        atr.Should().BeGreaterThan(0m);
    }

    private static List<Candle> CreateSampleCandles(int count, decimal startPrice, decimal step)
    {
        var candles = new List<Candle>();
        decimal price = startPrice;
        DateTime start = DateTime.UtcNow.AddHours(-count);

        for (int i = 0; i < count; i++)
        {
            decimal close = price + step;
            candles.Add(new Candle(
                start.AddHours(i),
                price,
                close + 0.5m,
                price - 0.5m,
                close,
                1000m + i * 10m));
            price = close;
        }

        return candles;
    }
}
