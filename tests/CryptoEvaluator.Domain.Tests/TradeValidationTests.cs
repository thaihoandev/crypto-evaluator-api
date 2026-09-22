using CryptoEvaluator.Domain.Entities;
using CryptoEvaluator.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace CryptoEvaluator.Domain.Tests;

public class TradeValidationTests
{
    [Fact]
    public void Create_LongTrade_WithValidParameters_ShouldSucceed()
    {
        // Act
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

        // Assert
        trade.Should().NotBeNull();
        trade.Symbol.Should().Be("BTCUSDT");
        trade.Direction.Should().Be(TradeDirection.Long);
        trade.Status.Should().Be(TradeStatus.Planned);
    }

    [Fact]
    public void Create_LongTrade_WithStopLossAboveEntry_ShouldThrowArgumentException()
    {
        // Act
        Action act = () => Trade.Create(
            userId: Guid.NewGuid(),
            symbol: "BTCUSDT",
            direction: TradeDirection.Long,
            timeframe: Timeframe.H1,
            entryPrice: 104500m,
            stopLoss: 105000m, // Invalid for Long
            takeProfit: 108500m,
            accountBalance: 10000m,
            riskPercent: 1.0m,
            leverage: 5m);

        // Assert
        act.Should().Throw<ArgumentException>()
           .WithMessage("*Stop Loss must be below Entry Price*");
    }

    [Fact]
    public void Create_ShortTrade_WithStopLossBelowEntry_ShouldThrowArgumentException()
    {
        // Act
        Action act = () => Trade.Create(
            userId: Guid.NewGuid(),
            symbol: "ETHUSDT",
            direction: TradeDirection.Short,
            timeframe: Timeframe.H4,
            entryPrice: 3500m,
            stopLoss: 3400m, // Invalid for Short
            takeProfit: 3200m,
            accountBalance: 5000m,
            riskPercent: 2.0m,
            leverage: 10m);

        // Assert
        act.Should().Throw<ArgumentException>()
           .WithMessage("*Stop Loss must be above Entry Price*");
    }
}
