using CryptoEvaluator.Application.Evaluations.Models;
using CryptoEvaluator.Application.Common.Options;
using CryptoEvaluator.Application.Indicators;
using CryptoEvaluator.Application.MarketData.Interfaces;
using CryptoEvaluator.Application.MarketAnalysis;
using CryptoEvaluator.Application.Prediction;
using CryptoEvaluator.Application.Risk;
using CryptoEvaluator.Application.Scoring;
using CryptoEvaluator.Application.Warnings;
using CryptoEvaluator.Domain.Entities;
using CryptoEvaluator.Domain.Enums;
using CryptoEvaluator.Domain.Models;
using MediatR;

namespace CryptoEvaluator.Application.MarketAnalysis.Queries.AnalyzeMarket;

public record ProposedTradeSetupDto(
    TradeDirection Direction,
    decimal EntryPrice,
    decimal StopLoss,
    decimal TakeProfit,
    decimal EntryZoneLow,
    decimal EntryZoneHigh,
    decimal? NearestSupport,
    decimal? NearestResistance,
    SetupLevelSource StopLossSource,
    SetupLevelSource TakeProfitSource,
    decimal RiskRewardRatio,
    decimal SetupScore,
    bool IsTradable,
    TradePredictionDto Prediction,
    IReadOnlyList<TradeWarning> Warnings);

public record MarketAnalysisResponse(
    string Symbol,
    Timeframe Timeframe,
    MarketSnapshotDto Snapshot,
    PredictionDataQualityDto DataQuality,
    MarketRecommendation Recommendation,
    string Rationale,
    IReadOnlyList<MarketAnalysisBlocker> BlockingReasons,
    ProposedTradeSetupDto LongSetup,
    ProposedTradeSetupDto ShortSetup,
    string Disclaimer = "Market analysis is informational only, not financial advice. Confirm the setup before placing an order.");

public record AnalyzeMarketQuery(string Symbol, Timeframe Timeframe)
    : IRequest<MarketAnalysisResponse>;

public class AnalyzeMarketQueryHandler : IRequestHandler<AnalyzeMarketQuery, MarketAnalysisResponse>
{
    private readonly IMarketDataProvider _marketDataProvider;
    private readonly IIndicatorEngine _indicatorEngine;
    private readonly ITradePredictionEngine _predictionEngine;
    private readonly IRiskCalculator _riskCalculator;
    private readonly ITradeScoreCalculator _scoreCalculator;
    private readonly IWarningEngine _warningEngine;
    private readonly MarketAnalysisOptions _options;
    private readonly IMarketStructureAnalyzer _marketStructureAnalyzer;

    public AnalyzeMarketQueryHandler(
        IMarketDataProvider marketDataProvider,
        IIndicatorEngine indicatorEngine,
        ITradePredictionEngine predictionEngine,
        IRiskCalculator riskCalculator,
        ITradeScoreCalculator scoreCalculator,
        IWarningEngine warningEngine,
        MarketAnalysisOptions options,
        IMarketStructureAnalyzer marketStructureAnalyzer)
    {
        _marketDataProvider = marketDataProvider;
        _indicatorEngine = indicatorEngine;
        _predictionEngine = predictionEngine;
        _riskCalculator = riskCalculator;
        _scoreCalculator = scoreCalculator;
        _warningEngine = warningEngine;
        _options = options;
        _marketStructureAnalyzer = marketStructureAnalyzer;
    }

    public async Task<MarketAnalysisResponse> Handle(AnalyzeMarketQuery request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Symbol))
            throw new ArgumentException("Symbol is required.", nameof(request.Symbol));

        var candles = await _marketDataProvider.GetCandlesAsync(
            request.Symbol, request.Timeframe, _options.CandleRequestLimit, cancellationToken);
        var closedCandles = candles.Where(c => c.CloseTime is null || c.CloseTime <= DateTime.UtcNow).ToList();
        if (closedCandles.Count < _options.MinimumClosedCandles)
            throw new InvalidOperationException(
                $"At least {_options.MinimumClosedCandles} closed candles are required for market analysis.");

        var indicators = _indicatorEngine.CalculateAll(closedCandles);
        var quality = BuildDataQuality(closedCandles, request.Timeframe, _options.MinimumClosedCandles);
        DateTime lastCandleTime = closedCandles[^1].OpenTime;
        var structure = _marketStructureAnalyzer.Analyze(
            closedCandles, indicators.CurrentPrice, _options.StructureLookbackCandles, _options.PivotStrength);

        var longSetup = BuildSetup(TradeDirection.Long);
        var shortSetup = BuildSetup(TradeDirection.Short);

        ProposedTradeSetupDto BuildSetup(TradeDirection direction)
        {
            decimal atr = indicators.Atr > 0m ? indicators.Atr : indicators.CurrentPrice * 0.01m;
            decimal entry = indicators.CurrentPrice;
            decimal atrStopDistance = Math.Max(atr * _options.StopLossAtrMultiplier, entry * 0.002m);
            decimal zoneBuffer = atr * _options.EntryZoneAtrMultiplier;
            decimal zoneCenter = direction == TradeDirection.Long
                ? structure.Support ?? indicators.Ema20
                : structure.Resistance ?? indicators.Ema20;
            decimal zoneLow = Math.Max(0.00000001m, zoneCenter - zoneBuffer);
            decimal zoneHigh = zoneCenter + zoneBuffer;
            bool hasStructureStop = direction == TradeDirection.Long
                ? structure.Support.HasValue && structure.Support.Value > 0m && structure.Support.Value < entry
                : structure.Resistance.HasValue && structure.Resistance.Value > entry;
            decimal stop = hasStructureStop
                ? direction == TradeDirection.Long
                    ? structure.Support!.Value - atr * _options.StructureStopBufferAtrMultiplier
                    : structure.Resistance!.Value + atr * _options.StructureStopBufferAtrMultiplier
                : direction == TradeDirection.Long ? entry - atrStopDistance : entry + atrStopDistance;
            var stopSource = hasStructureStop ? SetupLevelSource.MarketStructure : SetupLevelSource.AtrFallback;
            decimal riskDistance = Math.Abs(entry - stop);
            bool hasStructureTarget = direction == TradeDirection.Long
                ? structure.Resistance.HasValue && structure.Resistance.Value > entry
                : structure.Support.HasValue && structure.Support.Value > 0m && structure.Support.Value < entry;
            decimal take = hasStructureTarget
                ? direction == TradeDirection.Long ? structure.Resistance!.Value : structure.Support!.Value
                : direction == TradeDirection.Long
                    ? entry + riskDistance * _options.TakeProfitRiskRewardRatio
                    : entry - riskDistance * _options.TakeProfitRiskRewardRatio;
            var takeSource = hasStructureTarget ? SetupLevelSource.MarketStructure : SetupLevelSource.AtrFallback;

            var trade = Trade.Create(
                Guid.Empty, request.Symbol, direction, request.Timeframe,
                entry, stop, take, _options.PreviewAccountBalance, _options.PreviewRiskPercent, _options.PreviewLeverage);
            var risk = _riskCalculator.Calculate(trade);
            var score = _scoreCalculator.Calculate(trade, indicators, risk);
            int seed = HashCode.Combine(request.Symbol.ToUpperInvariant(), request.Timeframe, direction, lastCandleTime);
            var result = _predictionEngine.Predict(
                trade, indicators, closedCandles, lastCandleTime, seed, _options.SimulatedPaths);
            var prediction = new TradePredictionDto(
                result.WinProbability, result.LossProbability, result.NoHitProbability,
                result.ExpectedRMultiple, result.Method.ToString(), result.SimulatedPaths,
                result.SampleSize, result.RandomSeed, result.Disclaimer ?? string.Empty,
                result.TrajectoryPoints, result.ScenarioPaths, quality, result.Confidence);
            bool inEntryZone = entry >= zoneLow && entry <= zoneHigh;
            bool structureRrIsAcceptable = !hasStructureTarget
                || risk.RiskRewardRatio >= _options.MinimumStructureRiskRewardRatio;
            bool tradable = inEntryZone
                && structureRrIsAcceptable
                && score.TotalScore >= _options.MinimumSetupScore
                && result.WinProbability >= _options.MinimumWinProbability
                && result.ExpectedRMultiple > _options.MinimumExpectedRMultiple
                && result.NoHitProbability <= _options.MaximumNoHitProbability
                && result.Confidence?.Level is PredictionConfidenceLevel.Medium or PredictionConfidenceLevel.High
                && !quality.IsStale;

            return new ProposedTradeSetupDto(
                direction, entry, stop, take, zoneLow, zoneHigh, structure.Support, structure.Resistance,
                stopSource, takeSource, risk.RiskRewardRatio, score.TotalScore,
                tradable, prediction, _warningEngine.GenerateWarnings(trade, indicators, risk));
        }

        var setups = new[] { longSetup, shortSetup };
        var preferred = setups
            .Where(x => x.IsTradable)
            .OrderByDescending(x => x.SetupScore)
            .ThenByDescending(x => x.Prediction.WinProbability)
            .FirstOrDefault()
            ?? setups.OrderByDescending(x => x.SetupScore).First();
        var blockers = GetBlockingReasons(preferred, quality);
        MarketRecommendation recommendation = preferred.IsTradable
            ? preferred.Direction == TradeDirection.Long ? MarketRecommendation.Long : MarketRecommendation.Short
            : MarketRecommendation.Wait;
        string rationale = recommendation == MarketRecommendation.Wait
            ? $"No setup meets the entry requirements: {string.Join(", ", blockers)}."
            : $"{preferred.Direction} has the stronger setup score ({preferred.SetupScore:F1}/100) with {preferred.Prediction.Confidence?.Level.ToString().ToLowerInvariant()} confidence.";

        return new MarketAnalysisResponse(
            request.Symbol.ToUpperInvariant().Trim(), request.Timeframe,
            new MarketSnapshotDto(indicators.CurrentPrice, indicators.Ema20, indicators.Ema50,
                indicators.Ema200, indicators.Rsi, indicators.Atr, indicators.VolumeRatio),
            quality, recommendation, rationale, blockers, longSetup, shortSetup);
    }

    private IReadOnlyList<MarketAnalysisBlocker> GetBlockingReasons(
        ProposedTradeSetupDto setup,
        PredictionDataQualityDto quality)
    {
        var blockers = new List<MarketAnalysisBlocker>();
        if (!quality.IsSufficient) blockers.Add(MarketAnalysisBlocker.InsufficientClosedCandles);
        if (quality.IsStale) blockers.Add(MarketAnalysisBlocker.StaleMarketData);
        if (setup.Prediction.Confidence?.Level is PredictionConfidenceLevel.Low or null)
            blockers.Add(MarketAnalysisBlocker.LowConfidence);
        if (setup.SetupScore < _options.MinimumSetupScore) blockers.Add(MarketAnalysisBlocker.LowSetupScore);
        if (setup.Prediction.WinProbability < _options.MinimumWinProbability) blockers.Add(MarketAnalysisBlocker.LowWinProbability);
        if (setup.Prediction.ExpectedRMultiple <= _options.MinimumExpectedRMultiple) blockers.Add(MarketAnalysisBlocker.LowExpectedRMultiple);
        if (setup.Prediction.NoHitProbability > _options.MaximumNoHitProbability) blockers.Add(MarketAnalysisBlocker.HighNoHitProbability);
        if (setup.EntryPrice < setup.EntryZoneLow || setup.EntryPrice > setup.EntryZoneHigh)
            blockers.Add(MarketAnalysisBlocker.OutsideEntryZone);
        if (setup.TakeProfitSource == SetupLevelSource.MarketStructure
            && setup.RiskRewardRatio < _options.MinimumStructureRiskRewardRatio)
            blockers.Add(MarketAnalysisBlocker.PoorMarketStructureRiskReward);
        return blockers;
    }

    private static PredictionDataQualityDto BuildDataQuality(
        IReadOnlyList<CryptoEvaluator.Domain.Models.Candle> candles,
        Timeframe timeframe,
        int minimumClosedCandles)
    {
        TimeSpan duration = timeframe switch
        {
            Timeframe.M1 => TimeSpan.FromMinutes(1), Timeframe.M5 => TimeSpan.FromMinutes(5),
            Timeframe.M15 => TimeSpan.FromMinutes(15), Timeframe.M30 => TimeSpan.FromMinutes(30),
            Timeframe.H1 => TimeSpan.FromHours(1), Timeframe.H4 => TimeSpan.FromHours(4),
            Timeframe.D1 => TimeSpan.FromDays(1), _ => TimeSpan.FromHours(1)
        };
        DateTime lastClose = candles[^1].CloseTime ?? candles[^1].OpenTime + duration;
        int ageSeconds = Math.Max(0, (int)(DateTime.UtcNow - lastClose).TotalSeconds);
        return new PredictionDataQualityDto(candles.Count >= minimumClosedCandles, candles.Count, lastClose, ageSeconds,
            DateTime.UtcNow - lastClose > duration * 2, MarketDataSource.BinanceFutures);
    }
}
