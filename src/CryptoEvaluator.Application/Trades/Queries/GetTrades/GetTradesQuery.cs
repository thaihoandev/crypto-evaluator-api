using CryptoEvaluator.Application.Trades.Models;
using CryptoEvaluator.Domain.Enums;
using CryptoEvaluator.Domain.Interfaces;
using MediatR;

namespace CryptoEvaluator.Application.Trades.Queries.GetTrades;

public record GetTradesQuery(
    Guid UserId,
    string? Symbol = null,
    TradeDirection? Direction = null,
    TradeStatus? Status = null,
    decimal? MinScore = null,
    decimal? MaxScore = null) : IRequest<IReadOnlyList<TradeResponse>>;

public class GetTradesQueryHandler : IRequestHandler<GetTradesQuery, IReadOnlyList<TradeResponse>>
{
    private readonly ITradeRepository _tradeRepository;

    public GetTradesQueryHandler(ITradeRepository tradeRepository)
    {
        _tradeRepository = tradeRepository;
    }

    public async Task<IReadOnlyList<TradeResponse>> Handle(GetTradesQuery request, CancellationToken cancellationToken)
    {
        var trades = await _tradeRepository.GetUserTradesAsync(
            userId: request.UserId,
            symbol: request.Symbol,
            direction: request.Direction,
            status: request.Status,
            minScore: request.MinScore,
            maxScore: request.MaxScore,
            cancellationToken: cancellationToken);

        return trades.Select(TradeResponse.FromDomain).ToList();
    }
}
