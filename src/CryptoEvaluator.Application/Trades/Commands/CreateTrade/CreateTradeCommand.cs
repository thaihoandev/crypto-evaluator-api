using CryptoEvaluator.Application.Trades.Models;
using CryptoEvaluator.Domain.Entities;
using CryptoEvaluator.Domain.Enums;
using CryptoEvaluator.Domain.Interfaces;
using MediatR;

namespace CryptoEvaluator.Application.Trades.Commands.CreateTrade;

public record CreateTradeCommand(
    Guid UserId,
    string Symbol,
    TradeDirection Direction,
    Timeframe Timeframe,
    decimal EntryPrice,
    decimal StopLoss,
    decimal TakeProfit,
    decimal AccountBalance,
    decimal RiskPercent,
    decimal Leverage) : IRequest<TradeResponse>;

public class CreateTradeCommandHandler : IRequestHandler<CreateTradeCommand, TradeResponse>
{
    private readonly ITradeRepository _tradeRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CreateTradeCommandHandler(ITradeRepository tradeRepository, IUnitOfWork unitOfWork)
    {
        _tradeRepository = tradeRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<TradeResponse> Handle(CreateTradeCommand request, CancellationToken cancellationToken)
    {
        var trade = Trade.Create(
            userId: request.UserId,
            symbol: request.Symbol,
            direction: request.Direction,
            timeframe: request.Timeframe,
            entryPrice: request.EntryPrice,
            stopLoss: request.StopLoss,
            takeProfit: request.TakeProfit,
            accountBalance: request.AccountBalance,
            riskPercent: request.RiskPercent,
            leverage: request.Leverage);

        await _tradeRepository.AddAsync(trade, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return TradeResponse.FromDomain(trade);
    }
}
