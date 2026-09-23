using CryptoEvaluator.Application.Evaluations.Commands.EvaluateTrade;
using CryptoEvaluator.Application.Evaluations.Models;
using CryptoEvaluator.Application.MarketData.Interfaces;
using CryptoEvaluator.Application.MarketAnalysis.Queries.AnalyzeMarket;
using CryptoEvaluator.Application.Predictions.Queries.GetPrediction;
using CryptoEvaluator.Application.Predictions.Queries.GetPredictionBacktest;
using CryptoEvaluator.Application.Trades.Commands.CloseTrade;
using CryptoEvaluator.Application.Trades.Commands.CreateTrade;
using CryptoEvaluator.Application.Trades.Models;
using CryptoEvaluator.API.Serialization;
using CryptoEvaluator.Application.Trades.Queries.GetTrade;
using CryptoEvaluator.Application.Trades.Queries.GetTrades;
using CryptoEvaluator.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace CryptoEvaluator.API.Controllers;

[ApiController]
[Route("api/v1/trades")]
public class TradesController : ControllerBase
{
    private readonly ISender _sender;
    private readonly IMarketDataProvider _marketDataProvider;

    public TradesController(ISender sender, IMarketDataProvider marketDataProvider)
    {
        _sender = sender;
        _marketDataProvider = marketDataProvider;
    }

    /// <summary>
    /// Create a new trade setup
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(TradeResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateTrade([FromBody] CreateTradeRequest request, CancellationToken cancellationToken)
    {
        // Default UserId for MVP if not supplied
        Guid userId = request.UserId ?? Guid.Parse("00000000-0000-0000-0000-000000000001");

        var command = new CreateTradeCommand(
            UserId: userId,
            Symbol: request.Symbol,
            Direction: request.Direction,
            Timeframe: request.Timeframe,
            EntryPrice: request.EntryPrice,
            StopLoss: request.StopLoss,
            TakeProfit: request.TakeProfit,
            AccountBalance: request.AccountBalance,
            RiskPercent: request.RiskPercent,
            Leverage: request.Leverage);

        var result = await _sender.Send(command, cancellationToken);
        return CreatedAtAction(nameof(GetTrade), new { id = result.Id }, result);
    }

    /// <summary>
    /// Retrieve a trade setup by ID
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(TradeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTrade(Guid id, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetTradeQuery(id), cancellationToken);
        if (result == null)
            return NotFound(new { code = "TRADE_NOT_FOUND", message = $"Trade with ID '{id}' was not found." });

        return Ok(result);
    }

    /// <summary>
    /// List trades with optional filters
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<TradeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTrades(
        [FromQuery] Guid? userId,
        [FromQuery] string? symbol,
        [FromQuery] TradeDirection? direction,
        [FromQuery] TradeStatus? status,
        [FromQuery] decimal? minScore,
        [FromQuery] decimal? maxScore,
        CancellationToken cancellationToken)
    {
        Guid effectiveUserId = userId ?? Guid.Parse("00000000-0000-0000-0000-000000000001");

        var result = await _sender.Send(new GetTradesQuery(
            UserId: effectiveUserId,
            Symbol: symbol,
            Direction: direction,
            Status: status,
            MinScore: minScore,
            MaxScore: maxScore), cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Automatically fetch market data from provider, compute indicators, evaluate risk, calculate score breakdown, run Monte Carlo prediction, and return evaluation results.
    /// </summary>
    [HttpPost("{id:guid}/evaluate")]
    [ProducesResponseType(typeof(EvaluateTradeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> EvaluateTrade(Guid id, [FromBody] EvaluateTradeRequest? request, CancellationToken cancellationToken)
    {
        var command = new EvaluateTradeCommand(id, request?.RandomSeed);
        var result = await _sender.Send(command, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Close a trade and record trade journal entry
    /// </summary>
    [HttpPost("{id:guid}/close")]
    [ProducesResponseType(typeof(TradeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CloseTrade(Guid id, [FromBody] CloseTradeRequest request, CancellationToken cancellationToken)
    {
        var command = new CloseTradeCommand(id, request.ExitPrice, request.ExitReason, request.Notes);
        var result = await _sender.Send(command, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Get stored trade prediction details
    /// </summary>
    [HttpGet("{id:guid}/prediction")]
    [ProducesResponseType(typeof(TradePredictionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPrediction(Guid id, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetPredictionQuery(id), cancellationToken);
        if (result == null)
            return NotFound(new { code = "PREDICTION_NOT_FOUND", message = $"No prediction recorded for trade '{id}'." });

        return Ok(result);
    }

    /// <summary>
    /// Compare predictions made before trades closed with their realised outcomes.
    /// Brier score closer to zero means better-calibrated win probabilities.
    /// </summary>
    [HttpGet("predictions/backtest")]
    [ProducesResponseType(typeof(PredictionBacktestReportDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPredictionBacktest(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken cancellationToken)
    {
        from = NormalizeUtc(from);
        to = NormalizeUtc(to);

        if (from.HasValue && to.HasValue && from > to)
            return BadRequest(new { code = "INVALID_DATE_RANGE", message = "'from' must not be later than 'to'." });

        return Ok(await _sender.Send(new GetPredictionBacktestQuery(from, to), cancellationToken));
    }

    private static DateTime? NormalizeUtc(DateTime? value) =>
        value.HasValue ? UtcDateTimeJsonConverter.ToUtc(value.Value) : null;

    /// <summary>
    /// Get real-time price ticker for a specific crypto symbol from Binance
    /// </summary>
    [HttpGet("ticker/{symbol}")]
    public async Task<IActionResult> GetTicker(string symbol, CancellationToken cancellationToken)
    {
        var ticker = await _marketDataProvider.GetTickerAsync(symbol, cancellationToken);
        return Ok(new
        {
            symbol = ticker.Symbol,
            price = ticker.Price,
            timestamp = ticker.Timestamp
        });
    }

    /// <summary>
    /// Get dynamic list of supported Binance Futures USDT crypto pairs
    /// </summary>
    [HttpGet("symbols")]
    public async Task<IActionResult> GetSymbols(CancellationToken cancellationToken)
    {
        var symbols = await _marketDataProvider.GetSymbolsAsync(cancellationToken);
        return Ok(symbols);
    }

    /// <summary>
    /// Analyze a crypto market before creating a trade. Returns Long, Short, and
    /// Wait guidance with proposed ATR-based entry, stop, target, and prediction.
    /// </summary>
    [HttpGet("analyze/{symbol}")]
    [ProducesResponseType(typeof(MarketAnalysisResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AnalyzeMarket(
        string symbol,
        [FromQuery] Timeframe timeframe = Timeframe.H1,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new AnalyzeMarketQuery(symbol, timeframe), cancellationToken);
        return Ok(result);
    }
}

public record CreateTradeRequest(
    Guid? UserId,
    string Symbol,
    TradeDirection Direction,
    Timeframe Timeframe,
    decimal EntryPrice,
    decimal StopLoss,
    decimal TakeProfit,
    decimal AccountBalance,
    decimal RiskPercent,
    decimal Leverage);

public record EvaluateTradeRequest(int? RandomSeed = null);

public record CloseTradeRequest(
    decimal ExitPrice,
    string? ExitReason = null,
    string? Notes = null);
