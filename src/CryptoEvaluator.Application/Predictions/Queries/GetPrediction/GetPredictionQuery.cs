using CryptoEvaluator.Application.Evaluations.Models;
using CryptoEvaluator.Domain.Interfaces;
using MediatR;

namespace CryptoEvaluator.Application.Predictions.Queries.GetPrediction;

public record GetPredictionQuery(Guid TradeId) : IRequest<TradePredictionDto?>;

public class GetPredictionQueryHandler : IRequestHandler<GetPredictionQuery, TradePredictionDto?>
{
    private readonly ITradePredictionRepository _predictionRepository;

    public GetPredictionQueryHandler(ITradePredictionRepository predictionRepository)
    {
        _predictionRepository = predictionRepository;
    }

    public async Task<TradePredictionDto?> Handle(GetPredictionQuery request, CancellationToken cancellationToken)
    {
        var prediction = await _predictionRepository.GetByTradeIdAsync(request.TradeId, cancellationToken);
        if (prediction == null) return null;

        return new TradePredictionDto(
            WinProbability: prediction.WinProbability,
            LossProbability: prediction.LossProbability,
            NoHitProbability: prediction.NoHitProbability,
            ExpectedRMultiple: prediction.ExpectedRMultiple,
            Method: prediction.Method.ToString(),
            SimulatedPaths: 10000,
            SampleSize: prediction.SampleSize,
            RandomSeed: prediction.RandomSeed,
            Disclaimer: "Probability estimate based on simulation, not financial advice.");
    }
}
