using CryptoEvaluator.Application.MarketAnalysis;
using CryptoEvaluator.Domain.Models;
using FluentAssertions;
using Xunit;

namespace CryptoEvaluator.Application.Tests;

public class MarketStructureAnalyzerTests
{
    [Fact]
    public void Analyze_ConfirmedPivots_ReturnsNearestSupportAndResistance()
    {
        DateTime start = DateTime.UtcNow.AddHours(-8);
        var candles = new List<Candle>
        {
            Create(start, 100m, 95m), Create(start.AddHours(1), 105m, 97m),
            Create(start.AddHours(2), 102m, 96m), Create(start.AddHours(3), 98m, 90m),
            Create(start.AddHours(4), 103m, 95m), Create(start.AddHours(5), 110m, 100m),
            Create(start.AddHours(6), 106m, 99m), Create(start.AddHours(7), 104m, 98m)
        };

        var result = new MarketStructureAnalyzer().Analyze(candles, currentPrice: 100m, lookbackCandles: 50, pivotStrength: 1);

        result.Support.Should().Be(90m);
        result.Resistance.Should().Be(105m);
    }

    private static Candle Create(DateTime openTime, decimal high, decimal low) =>
        new(openTime, (high + low) / 2m, high, low, (high + low) / 2m, 100m,
            openTime.AddHours(1).AddMilliseconds(-1));
}
