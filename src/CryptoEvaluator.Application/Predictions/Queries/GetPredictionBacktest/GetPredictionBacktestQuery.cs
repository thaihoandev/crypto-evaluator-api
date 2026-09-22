using CryptoEvaluator.Domain.Interfaces;
using MediatR;

namespace CryptoEvaluator.Application.Predictions.Queries.GetPredictionBacktest;

public record CalibrationBucketDto(
    string Range,
    int SampleSize,
    decimal AveragePredictedWinProbability,
    decimal ActualWinRate);

public record PredictionBacktestReportDto(
    int ResolvedPredictions,
    decimal AveragePredictedWinProbability,
    decimal ActualWinRate,
    decimal BrierScore,
    decimal MeanAbsoluteProbabilityError,
    decimal AverageExpectedRMultiple,
    decimal AverageRealizedRMultiple,
    IReadOnlyList<CalibrationBucketDto> Calibration);

public record GetPredictionBacktestQuery(DateTime? From, DateTime? To)
    : IRequest<PredictionBacktestReportDto>;

public class GetPredictionBacktestQueryHandler
    : IRequestHandler<GetPredictionBacktestQuery, PredictionBacktestReportDto>
{
    private readonly ITradePredictionRepository _repository;

    public GetPredictionBacktestQueryHandler(ITradePredictionRepository repository) => _repository = repository;

    public async Task<PredictionBacktestReportDto> Handle(GetPredictionBacktestQuery request, CancellationToken cancellationToken)
    {
        var outcomes = await _repository.GetResolvedOutcomesAsync(request.From, request.To, cancellationToken);
        if (outcomes.Count == 0)
            return new PredictionBacktestReportDto(0, 0m, 0m, 0m, 0m, 0m, 0m, []);

        decimal Brier(CryptoEvaluator.Domain.Models.PredictionOutcome x)
        {
            decimal predicted = x.PredictedWinProbability / 100m;
            decimal actual = x.IsWin ? 1m : 0m;
            return (predicted - actual) * (predicted - actual);
        }

        var calibration = Enumerable.Range(0, 5)
            .Select(bucket =>
            {
                decimal lower = bucket * 20m;
                decimal upper = lower + 20m;
                var samples = outcomes.Where(x => bucket == 4
                    ? x.PredictedWinProbability >= lower && x.PredictedWinProbability <= upper
                    : x.PredictedWinProbability >= lower && x.PredictedWinProbability < upper).ToList();
                return new CalibrationBucketDto(
                    $"{lower:0}-{upper:0}%",
                    samples.Count,
                    samples.Count == 0 ? 0m : Math.Round(samples.Average(x => x.PredictedWinProbability), 2),
                    samples.Count == 0 ? 0m : Math.Round(samples.Count(x => x.IsWin) * 100m / samples.Count, 2));
            })
            .ToList();

        return new PredictionBacktestReportDto(
            outcomes.Count,
            Math.Round(outcomes.Average(x => x.PredictedWinProbability), 2),
            Math.Round(outcomes.Count(x => x.IsWin) * 100m / outcomes.Count, 2),
            Math.Round(outcomes.Average(Brier), 4),
            Math.Round(outcomes.Average(x => Math.Abs(x.PredictedWinProbability - (x.IsWin ? 100m : 0m))), 2),
            Math.Round(outcomes.Average(x => x.ExpectedRMultiple), 4),
            Math.Round(outcomes.Average(x => x.RealizedRMultiple), 4),
            calibration);
    }
}
