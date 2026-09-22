using CryptoEvaluator.Application.Risk;
using CryptoEvaluator.Domain.Entities;
using CryptoEvaluator.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace CryptoEvaluator.Application.Tests;

public class RiskCalculatorTests
{
    private readonly RiskCalculator _calculator = new();

    [Fact]
    public void Calculate_LongTrade_ShouldComputeCorrectRiskAmountAndPositionSize()
    {
        // Arrange
        // Entry: 100, SL: 98, TP: 106. SL Distance = 2, TP Distance = 6. RR = 3. RiskAmount = 10000 * 1% = $100
        var trade = Trade.Create(
            userId: Guid.NewGuid(),
            symbol: "BTCUSDT",
            direction: TradeDirection.Long,
            timeframe: Timeframe.H1,
            entryPrice: 100m,
            stopLoss: 98m,
            takeProfit: 106m,
            accountBalance: 10000m,
            riskPercent: 1.0m,
            leverage: 5m);

        // Act
        var result = _calculator.Calculate(trade);

        // Assert
        result.RiskAmount.Should().Be(100m);
        result.StopLossDistance.Should().Be(2m);
        result.TakeProfitDistance.Should().Be(6m);
        result.RiskRewardRatio.Should().Be(3m);
        result.PositionSize.Should().Be(50m); // $100 / $2
        result.MarginRequired.Should().Be(1000m); // (50 * 100) / 5
    }
}
