using CryptoEvaluator.Application.Trades.Models;
using CryptoEvaluator.Domain.Entities;
using CryptoEvaluator.Domain.Enums;
using CryptoEvaluator.Domain.Interfaces;
using MediatR;

namespace CryptoEvaluator.Application.Trades.Commands.CloseTrade;

public record CloseTradeCommand(
    Guid TradeId,
    decimal ExitPrice,
    string? ExitReason = null,
    string? Notes = null) : IRequest<TradeResponse>;

public class CloseTradeCommandHandler : IRequestHandler<CloseTradeCommand, TradeResponse>
{
    private readonly ITradeRepository _tradeRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CloseTradeCommandHandler(ITradeRepository tradeRepository, IUnitOfWork unitOfWork)
    {
        _tradeRepository = tradeRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<TradeResponse> Handle(CloseTradeCommand request, CancellationToken cancellationToken)
    {
        var trade = await _tradeRepository.GetByIdAsync(request.TradeId, cancellationToken);
        if (trade == null)
            throw new KeyNotFoundException($"Trade with ID '{request.TradeId}' was not found.");

        decimal pnl;
        decimal slDistance;
        bool isWin;

        if (trade.Direction == TradeDirection.Long)
        {
            slDistance = trade.EntryPrice - trade.StopLoss;
            pnl = (request.ExitPrice - trade.EntryPrice) * (trade.AccountBalance * (trade.RiskPercent / 100m) / slDistance);
            isWin = request.ExitPrice > trade.EntryPrice;
        }
        else
        {
            slDistance = trade.StopLoss - trade.EntryPrice;
            pnl = (trade.EntryPrice - request.ExitPrice) * (trade.AccountBalance * (trade.RiskPercent / 100m) / slDistance);
            isWin = request.ExitPrice < trade.EntryPrice;
        }

        decimal riskAmount = trade.AccountBalance * (trade.RiskPercent / 100m);
        decimal rMultiple = riskAmount > 0 ? pnl / riskAmount : 0m;

        var result = TradeResult.Create(
            tradeId: trade.Id,
            exitPrice: request.ExitPrice,
            pnl: pnl,
            rMultiple: rMultiple,
            isWin: isWin,
            exitReason: request.ExitReason,
            notes: request.Notes);

        trade.Close(result);
        _tradeRepository.Update(trade);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return TradeResponse.FromDomain(trade);
    }
}
