using CryptoEvaluator.Application.Trades.Models;
using CryptoEvaluator.Domain.Enums;
using CryptoEvaluator.Domain.Interfaces;
using MediatR;

namespace CryptoEvaluator.Application.Trades.Queries.GetTrade;

public record GetTradeQuery(Guid TradeId) : IRequest<TradeResponse?>;

public class GetTradeQueryHandler : IRequestHandler<GetTradeQuery, TradeResponse?>
{
    private readonly ITradeRepository _tradeRepository;

    public GetTradeQueryHandler(ITradeRepository tradeRepository)
    {
        _tradeRepository = tradeRepository;
    }

    public async Task<TradeResponse?> Handle(GetTradeQuery request, CancellationToken cancellationToken)
    {
        var trade = await _tradeRepository.GetByIdAsync(request.TradeId, cancellationToken);
        return trade != null ? TradeResponse.FromDomain(trade) : null;
    }
}
