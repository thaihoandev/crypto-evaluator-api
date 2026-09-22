using CryptoEvaluator.Application.Evaluations.Models;
using CryptoEvaluator.Application.Indicators;
using CryptoEvaluator.Application.MarketData.Interfaces;
using CryptoEvaluator.Application.Prediction;
using CryptoEvaluator.Application.Risk;
using CryptoEvaluator.Application.Scoring;
using CryptoEvaluator.Application.Warnings;
using CryptoEvaluator.Domain.Entities;
using CryptoEvaluator.Domain.Interfaces;
using MediatR;

namespace CryptoEvaluator.Application.Evaluations.Commands.EvaluateTrade;

public record EvaluateTradeCommand(
    Guid TradeId,
    int? RandomSeed = null) : IRequest<EvaluateTradeResponse>;

public class EvaluateTradeCommandHandler : IRequestHandler<EvaluateTradeCommand, EvaluateTradeResponse>
{
    private readonly ITradeRepository _tradeRepository;
    private readonly ITradeEvaluationRepository _evaluationRepository;
    private readonly ITradePredictionRepository _predictionRepository;
    private readonly IMarketDataProvider _marketDataProvider;
    private readonly IIndicatorEngine _indicatorEngine;
    private readonly IRiskCalculator _riskCalculator;
    private readonly ITradeScoreCalculator _scoreCalculator;
    private readonly ITradePredictionEngine _predictionEngine;
    private readonly IWarningEngine _warningEngine;
    private readonly IUnitOfWork _unitOfWork;

    public EvaluateTradeCommandHandler(
        ITradeRepository tradeRepository,
        ITradeEvaluationRepository evaluationRepository,
        ITradePredictionRepository predictionRepository,
        IMarketDataProvider marketDataProvider,
        IIndicatorEngine indicatorEngine,
        IRiskCalculator riskCalculator,
        ITradeScoreCalculator scoreCalculator,
        ITradePredictionEngine predictionEngine,
        IWarningEngine warningEngine,
        IUnitOfWork unitOfWork)
    {
        _tradeRepository = tradeRepository;
        _evaluationRepository = evaluationRepository;
        _predictionRepository = predictionRepository;
        _marketDataProvider = marketDataProvider;
        _indicatorEngine = indicatorEngine;
        _riskCalculator = riskCalculator;
        _scoreCalculator = scoreCalculator;
        _predictionEngine = predictionEngine;
        _warningEngine = warningEngine;
        _unitOfWork = unitOfWork;
    }

    public async Task<EvaluateTradeResponse> Handle(EvaluateTradeCommand request, CancellationToken cancellationToken)
    {
        var trade = await _tradeRepository.GetByIdAsync(request.TradeId, cancellationToken);
        if (trade == null)
        {
            throw new KeyNotFoundException($"Trade with ID '{request.TradeId}' was not found.");
        }

        // 1. Fetch Market Data from Market Provider
        var candles = await _marketDataProvider.GetCandlesAsync(
            trade.Symbol, trade.Timeframe, limit: 250, cancellationToken);

        // Never calibrate on Binance's still-open candle: its close/high/low change
        // until the interval ends and make the prediction non-reproducible.
        var closedCandles = candles
            .Where(c => c.CloseTime is null || c.CloseTime <= DateTime.UtcNow)
            .ToList();

        if (closedCandles.Count < 30)
        {
            throw new InvalidOperationException($"At least 30 closed market candles are required to evaluate '{trade.Symbol}' on timeframe '{trade.Timeframe}'.");
        }

        // 2. Calculate Indicators
        var indicators = _indicatorEngine.CalculateAll(closedCandles);

        // 3. Calculate Risk & Position Sizing
        var risk = _riskCalculator.Calculate(trade);

        // 4. Calculate Weighted Score Breakdown
        var scoreResult = _scoreCalculator.Calculate(trade, indicators, risk);

        int effectiveSeed = request.RandomSeed ?? HashCode.Combine(
            trade.Id,
            trade.Symbol,
            trade.Direction,
            trade.EntryPrice,
            trade.StopLoss,
            trade.TakeProfit,
            trade.Timeframe);

        // Last candle's OpenTime is the anchor for trajectory timestamps
        DateTime lastCandleTime = closedCandles[^1].OpenTime;

        // 5. Predict Outcome via Monte Carlo GBM
        var predictionResult = _predictionEngine.Predict(
            trade, indicators, closedCandles, lastCandleTime, effectiveSeed, numPaths: 20000);

        // 6. Generate Risk Warnings
        var warnings = _warningEngine.GenerateWarnings(trade, indicators, risk);

        // 7. Increment Version and Save Evaluation & Snapshot
        int latestVersion = await _evaluationRepository.GetLatestVersionAsync(trade.Id, cancellationToken);
        int currentVersion = latestVersion + 1;

        string explanation = $"Evaluated trade setup for {trade.Symbol} ({trade.Direction}). " +
                             $"Final Score: {scoreResult.TotalScore}/100. Win probability estimated at {predictionResult.WinProbability}%.";

        var evaluation = TradeEvaluation.Create(
            tradeId: trade.Id,
            version: currentVersion,
            riskRewardRatio: risk.RiskRewardRatio,
            riskAmount: risk.RiskAmount,
            positionSize: risk.PositionSize,
            marginRequired: risk.MarginRequired,
            score: scoreResult.TotalScore,
            explanation: explanation);

        var snapshot = MarketSnapshot.Create(
            tradeId: trade.Id,
            currentPrice: indicators.CurrentPrice,
            ema20: indicators.Ema20,
            ema50: indicators.Ema50,
            ema200: indicators.Ema200,
            rsi: indicators.Rsi,
            atr: indicators.Atr,
            volume: indicators.Volume,
            averageVolume: indicators.AverageVolume,
            volumeRatio: indicators.VolumeRatio,
            timestamp: indicators.Timestamp);

        var predictionEntity = TradePrediction.CreateMonteCarlo(
            tradeId: trade.Id,
            evaluationId: evaluation.Id,
            winProbability: predictionResult.WinProbability,
            lossProbability: predictionResult.LossProbability,
            noHitProbability: predictionResult.NoHitProbability,
            expectedRMultiple: predictionResult.ExpectedRMultiple,
            randomSeed: predictionResult.RandomSeed);

        await _evaluationRepository.AddAsync(evaluation, snapshot, cancellationToken);
        await _predictionRepository.AddAsync(predictionEntity, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var candleDtos = closedCandles.TakeLast(60).Select(c => new CandleDto(
            OpenTime: c.OpenTime,
            Open: c.Open,
            High: c.High,
            Low: c.Low,
            Close: c.Close,
            Volume: c.Volume
        )).ToList();

        return new EvaluateTradeResponse(
            TradeId: trade.Id,
            EvaluationId: evaluation.Id,
            Version: currentVersion,
            Score: scoreResult.TotalScore,
            RiskRewardRatio: risk.RiskRewardRatio,
            RiskAmount: risk.RiskAmount,
            PositionSize: risk.PositionSize,
            MarginRequired: risk.MarginRequired,
            Components: scoreResult.Components,
            Snapshot: new MarketSnapshotDto(
                CurrentPrice: indicators.CurrentPrice,
                Ema20: indicators.Ema20,
                Ema50: indicators.Ema50,
                Ema200: indicators.Ema200,
                Rsi: indicators.Rsi,
                Atr: indicators.Atr,
                VolumeRatio: indicators.VolumeRatio),
            Prediction: new TradePredictionDto(
                WinProbability: predictionResult.WinProbability,
                LossProbability: predictionResult.LossProbability,
                NoHitProbability: predictionResult.NoHitProbability,
                ExpectedRMultiple: predictionResult.ExpectedRMultiple,
                Method: predictionResult.Method.ToString(),
                SimulatedPaths: predictionResult.SimulatedPaths,
                SampleSize: predictionResult.SampleSize,
                RandomSeed: predictionResult.RandomSeed,
                Disclaimer: predictionResult.Disclaimer ?? string.Empty,
                TrajectoryPoints: predictionResult.TrajectoryPoints,
                ScenarioPaths: predictionResult.ScenarioPaths,
                DataQuality: BuildDataQuality(closedCandles, trade.Timeframe),
                Confidence: predictionResult.Confidence),
            Warnings: warnings,
            Explanation: explanation,
            Candles: candleDtos);
    }

    private static PredictionDataQualityDto BuildDataQuality(
        IReadOnlyList<CryptoEvaluator.Domain.Models.Candle> closedCandles,
        CryptoEvaluator.Domain.Enums.Timeframe timeframe)
    {
        TimeSpan duration = timeframe switch
        {
            CryptoEvaluator.Domain.Enums.Timeframe.M1 => TimeSpan.FromMinutes(1),
            CryptoEvaluator.Domain.Enums.Timeframe.M5 => TimeSpan.FromMinutes(5),
            CryptoEvaluator.Domain.Enums.Timeframe.M15 => TimeSpan.FromMinutes(15),
            CryptoEvaluator.Domain.Enums.Timeframe.M30 => TimeSpan.FromMinutes(30),
            CryptoEvaluator.Domain.Enums.Timeframe.H1 => TimeSpan.FromHours(1),
            CryptoEvaluator.Domain.Enums.Timeframe.H4 => TimeSpan.FromHours(4),
            CryptoEvaluator.Domain.Enums.Timeframe.D1 => TimeSpan.FromDays(1),
            _ => TimeSpan.FromHours(1)
        };
        DateTime lastClose = closedCandles[^1].CloseTime ?? closedCandles[^1].OpenTime + duration;
        int ageSeconds = Math.Max(0, (int)(DateTime.UtcNow - lastClose).TotalSeconds);
        bool isStale = DateTime.UtcNow - lastClose > duration * 2;

        return new PredictionDataQualityDto(
            IsSufficient: closedCandles.Count >= 30,
            ClosedCandleCount: closedCandles.Count,
            LastClosedCandleTime: lastClose,
            DataAgeSeconds: ageSeconds,
            IsStale: isStale,
            Source: "Binance Futures live candles");
    }
}
