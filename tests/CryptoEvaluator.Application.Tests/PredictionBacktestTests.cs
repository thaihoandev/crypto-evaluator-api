using CryptoEvaluator.Application.Predictions.Queries.GetPredictionBacktest;
using CryptoEvaluator.Domain.Interfaces;
using CryptoEvaluator.Domain.Models;
using FluentAssertions;
using Moq;
using Xunit;

namespace CryptoEvaluator.Application.Tests;

public class PredictionBacktestTests
{
    [Fact]
    public async Task Handle_ResolvedPredictions_ReturnsCalibrationAndBrierScore()
    {
        var outcomes = new List<PredictionOutcome>
        {
            new(80m, 1.5m, true, 1.8m, DateTime.UtcNow.AddDays(-3), DateTime.UtcNow.AddDays(-2)),
            new(20m, -0.5m, false, -1m, DateTime.UtcNow.AddDays(-3), DateTime.UtcNow.AddDays(-2))
        };
        var repository = new Mock<ITradePredictionRepository>();
        repository.Setup(x => x.GetResolvedOutcomesAsync(null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(outcomes);
        var handler = new GetPredictionBacktestQueryHandler(repository.Object);

        var result = await handler.Handle(new GetPredictionBacktestQuery(null, null), CancellationToken.None);

        result.ResolvedPredictions.Should().Be(2);
        result.ActualWinRate.Should().Be(50m);
        result.BrierScore.Should().Be(0.04m);
        result.Calibration.Should().HaveCount(5);
        result.Calibration.Single(x => x.Range == "0-20%").ActualWinRate.Should().Be(0m);
        result.Calibration.Single(x => x.Range == "80-100%").ActualWinRate.Should().Be(100m);
    }
}
