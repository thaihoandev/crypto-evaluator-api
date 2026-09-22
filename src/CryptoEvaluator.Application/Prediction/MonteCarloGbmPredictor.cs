using CryptoEvaluator.Application.Evaluations.Models;
using CryptoEvaluator.Domain.Entities;
using CryptoEvaluator.Domain.Enums;
using CryptoEvaluator.Domain.Models;

namespace CryptoEvaluator.Application.Prediction;

public record TradePredictionResult(
    decimal WinProbability,
    decimal LossProbability,
    decimal NoHitProbability,
    decimal ExpectedRMultiple,
    int SimulatedPaths,
    PredictionMethod Method,
    int? RandomSeed,
    int? SampleSize = null,
    string? Disclaimer = "Quantitative indicator & ATR prediction model, not financial advice.",
    IReadOnlyList<TrajectoryPointDto>? TrajectoryPoints = null);

public interface ITradePredictionEngine
{
    TradePredictionResult Predict(
        Trade trade,
        IndicatorSnapshot indicators,
        IReadOnlyList<Candle> candles,
        DateTime lastCandleTime,
        int? randomSeed = null,
        int numPaths = 10000);
}

public class MonteCarloGbmPredictor : ITradePredictionEngine
{
    // Number of candle steps to project into the future.
    // Represents the max holding period (e.g. 50 candles of the trade's timeframe).
    private const int ProjectionSteps = 50;

    // How many equi-spaced steps to include in the returned trajectory
    private const int TrajectoryResolution = 25;

    public TradePredictionResult Predict(
        Trade trade,
        IndicatorSnapshot indicators,
        IReadOnlyList<Candle> candles,
        DateTime lastCandleTime,
        int? randomSeed = null,
        int numPaths = 10000)
    {
        if (trade == null) throw new ArgumentNullException(nameof(trade));
        if (indicators == null) throw new ArgumentNullException(nameof(indicators));

        bool isLong = trade.Direction == TradeDirection.Long;
        decimal entry = trade.EntryPrice;
        decimal sl    = trade.StopLoss;
        decimal tp    = trade.TakeProfit;
        decimal atr   = indicators.Atr > 0 ? indicators.Atr : Math.Abs(tp - entry) * 0.15m;

        decimal slDist = Math.Abs(entry - sl);
        decimal tpDist = Math.Abs(tp - entry);
        decimal rr     = slDist > 0 ? tpDist / slDist : 1.0m;

        // GBM parameters — two drift values:
        //   muMarket : pure historical drift (no direction bias) → used for trajectory
        //   muTrade  : historical drift + indicator alignment bias → used for win/loss
        //   sigma    : historical volatility (same for both)
        var (muMarket, muTrade, sigma) = CalibrateFromCandles(candles, trade, indicators);

        // dt = 1 candle step (normalised)
        const double dt = 1.0;
        double sqrtDt   = Math.Sqrt(dt);

        // ── Run N simulated paths ──────────────────────────────────────────────
        var rng = randomSeed.HasValue ? new Random(randomSeed.Value) : new Random();

        int wins   = 0;
        int losses = 0;
        int noHits = 0;
        double totalR = 0.0;

        // ── Two separate tracking arrays ──────────────────────────────────────
        // 1. marketPricesByStep : raw GBM price, NO clamping → used for trajectory
        //    Uses muMarket (pure historical, no direction bias) so the displayed
        //    trajectory reflects where the market would go, not the trade parameters.
        // 2. TP/SL hit tracking via wins/losses/noHits → win/loss probability
        //    Uses muTrade (historical + indicator bias aligned with direction).
        var marketPricesByStep = new List<double>[ProjectionSteps + 1];
        for (int i = 0; i <= ProjectionSteps; i++)
            marketPricesByStep[i] = new List<double>(numPaths);

        int[] tpHitsByStep = new int[ProjectionSteps + 1];
        int[] slHitsByStep = new int[ProjectionSteps + 1];

        double entryD = (double)entry;
        double tpD    = (double)tp;
        double slD    = (double)sl;
        double targetAnchorD = (double)(indicators.Ema20 > 0 && indicators.Ema50 > 0
            ? (indicators.Ema20 + indicators.Ema50) / 2m
            : indicators.CurrentPrice > 0 ? indicators.CurrentPrice : trade.EntryPrice);

        for (int p = 0; p < numPaths; p++)
        {
            double marketPrice = entryD;
            marketPricesByStep[0].Add(marketPrice);

            double tradePrice = entryD;
            bool hitTp = false;
            bool hitSl = false;
            bool terminated = false;
            int tpStepHit = -1;
            int slStepHit = -1;

            for (int step = 1; step <= ProjectionSteps; step++)
            {
                // Fat-tailed noise sampling: Student's t-distribution with df=5
                double z = SampleFatTailedNoise(rng, df: 5.0);

                // Volatility decay over holding steps (GARCH-like decay to baseline)
                double stepSigma = sigma * Math.Exp(-0.02 * step);

                // Drift decay over holding period
                double stepMuMarket = muMarket * Math.Exp(-0.03 * step);
                double stepMuTrade  = muTrade  * Math.Exp(-0.03 * step);

                // Ornstein-Uhlenbeck Mean Reversion Pull towards key EMA level (theta = 0.04)
                double meanReversionPull = 0.04 * Math.Log(targetAnchorD / Math.Max(1e-4, marketPrice));
                double stepMuMarketAdjusted = stepMuMarket + meanReversionPull;

                // ── Path A: Pure market trajectory using muMarket & mean reversion
                marketPrice *= Math.Exp((stepMuMarketAdjusted - 0.5 * stepSigma * stepSigma) * dt + stepSigma * sqrtDt * z);
                marketPricesByStep[step].Add(marketPrice);

                // ── Path B: Trade outcome evaluation using muTrade (indicator & trend aligned drift)
                tradePrice *= Math.Exp((stepMuTrade - 0.5 * stepSigma * stepSigma) * dt + stepSigma * sqrtDt * z);

                // Check TP / SL — only count outcome once per path
                if (!terminated)
                {
                    bool tpHit = isLong ? tradePrice >= tpD : tradePrice <= tpD;
                    bool slHit = isLong ? tradePrice <= slD : tradePrice >= slD;

                    if (tpHit || slHit)
                    {
                        terminated = true;
                        // If both triggered on same candle, conservative → SL
                        hitTp = tpHit && !slHit;
                        hitSl = !hitTp;
                        if (hitTp) tpStepHit = step;
                        if (hitSl) slStepHit = step;
                    }
                }
            }

            if (tpStepHit > 0)
            {
                for (int s = tpStepHit; s <= ProjectionSteps; s++)
                    tpHitsByStep[s]++;
            }
            if (slStepHit > 0)
            {
                for (int s = slStepHit; s <= ProjectionSteps; s++)
                    slHitsByStep[s]++;
            }

            // Outcome tallying
            if (hitTp)
            {
                wins++;
                totalR += (double)rr;
            }
            else if (hitSl)
            {
                losses++;
                totalR -= 1.0;
            }
            else
            {
                noHits++;
                // Partial R based on final unrealised price vs trade boundaries
                double finalPrice = marketPricesByStep[ProjectionSteps][^1];
                double partialR   = isLong
                    ? (finalPrice - entryD) / (double)slDist
                    : (entryD - finalPrice) / (double)slDist;
                totalR += partialR;
            }
        }

        decimal winProb    = Math.Round((decimal)wins   / numPaths * 100m, 2);
        decimal lossProb   = Math.Round((decimal)losses / numPaths * 100m, 2);
        decimal noHitProb  = Math.Round((decimal)noHits / numPaths * 100m, 2);
        decimal expectedR  = Math.Round((decimal)(totalR / numPaths), 4);

        // ── Build trajectory from raw market percentile envelope ──────────────
        // Uses marketPricesByStep (unclamped) so the displayed path reflects
        // natural market movement, independent of the trade's TP/SL boundaries.
        // Each step maps to srcStep actual candles forward from lastCandleTime.
        var trajectoryPoints = new List<TrajectoryPointDto>(TrajectoryResolution + 1);
        double stepSize = (double)ProjectionSteps / TrajectoryResolution;
        TimeSpan candleDuration = TimeframeToCandleDuration(trade.Timeframe);

        for (int t = 0; t <= TrajectoryResolution; t++)
        {
            int srcStep = (int)Math.Round(t * stepSize);
            srcStep = Math.Clamp(srcStep, 0, ProjectionSteps);

            var prices = marketPricesByStep[srcStep];
            prices.Sort();

            // P10 = lower corridor, P50 = median trajectory, P90 = upper corridor
            decimal p10 = (decimal)Percentile(prices, 0.10);
            decimal p50 = (decimal)Percentile(prices, 0.50);
            decimal p90 = (decimal)Percentile(prices, 0.90);

            decimal expOpen = srcStep == 0 ? entry : (decimal)Percentile(marketPricesByStep[srcStep - 1], 0.50);
            decimal expClose = p50;
            decimal expHigh = p90;
            decimal expLow = p10;

            decimal cumTpHitProb = Math.Round((decimal)tpHitsByStep[srcStep] / numPaths * 100m, 2);
            decimal cumSlHitProb = Math.Round((decimal)slHitsByStep[srcStep] / numPaths * 100m, 2);

            // Timestamp = last historical candle open + srcStep candles forward
            DateTime stepTimestamp = lastCandleTime + candleDuration * srcStep;

            trajectoryPoints.Add(new TrajectoryPointDto(
                Step:                t,
                Price:               Math.Round(p50, 4),
                UpperBound:          Math.Round(p90, 4),
                LowerBound:          Math.Round(p10, 4),
                Timestamp:           stepTimestamp,
                ExpectedOpen:        Math.Round(expOpen, 4),
                ExpectedHigh:        Math.Round(expHigh, 4),
                ExpectedLow:         Math.Round(expLow, 4),
                ExpectedClose:       Math.Round(expClose, 4),
                CumulativeTpHitProb: cumTpHitProb,
                CumulativeSlHitProb: cumSlHitProb));
        }

        return new TradePredictionResult(
            WinProbability:   winProb,
            LossProbability:  lossProb,
            NoHitProbability: noHitProb,
            ExpectedRMultiple: expectedR,
            SimulatedPaths:   numPaths,
            Method:           PredictionMethod.MonteCarloGbm,
            RandomSeed:       randomSeed,
            TrajectoryPoints: trajectoryPoints);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Derives per-candle GBM parameters from historical candle log returns.
    /// Returns three values:
    ///   muMarket : pure historical drift (no direction bias) — for trajectory display
    ///   muTrade  : historical drift + small indicator-alignment bias — for TP/SL hit rate
    ///   sigma    : sample std-dev of log returns — used by both
    /// </summary>
    private static (double muMarket, double muTrade, double sigma) CalibrateFromCandles(
        IReadOnlyList<Candle> candles,
        Trade trade,
        IndicatorSnapshot indicators)
    {
        // ── 1. Compute log returns from historical closes ──────────────────────
        var logReturns = new List<double>(candles.Count);
        for (int i = 1; i < candles.Count; i++)
        {
            if (candles[i - 1].Close > 0 && candles[i].Close > 0)
                logReturns.Add(Math.Log((double)(candles[i].Close / candles[i - 1].Close)));
        }

        // ── 1. Exponentially Weighted Historical Drift (muEma) ─────────────────
        double muEma = 0.0;
        double sigmaHistorical = 0.02;

        if (logReturns.Count >= 2)
        {
            double alpha = 2.0 / (logReturns.Count + 1.0);
            muEma = logReturns[0];
            for (int i = 1; i < logReturns.Count; i++)
            {
                muEma = alpha * logReturns[i] + (1.0 - alpha) * muEma;
            }

            double variance = logReturns
                .Sum(r => (r - muEma) * (r - muEma))
                / (logReturns.Count - 1);
            sigmaHistorical = Math.Sqrt(variance);
        }

        // ── 2. Compute Parkinson Range Volatility & Close-to-Close Volatility ──
        double sumLogHL2 = 0.0;
        int validRanges = 0;
        for (int i = 0; i < candles.Count; i++)
        {
            if (candles[i].Low > 0 && candles[i].High >= candles[i].Low)
            {
                double logHL = Math.Log((double)(candles[i].High / candles[i].Low));
                sumLogHL2 += logHL * logHL;
                validRanges++;
            }
        }

        double sigmaParkinson = validRanges > 0
            ? Math.Sqrt(sumLogHL2 / (4.0 * Math.Log(2.0) * validRanges))
            : sigmaHistorical;

        // Blended Volatility (50% Close-to-Close + 50% Parkinson High/Low Range)
        double sigmaBlended = Math.Sqrt(0.5 * sigmaHistorical * sigmaHistorical + 0.5 * sigmaParkinson * sigmaParkinson);

        // ── 3. Market Technical Trend Momentum (Unbiased for muMarket) ────────
        decimal price = indicators.CurrentPrice > 0 ? indicators.CurrentPrice : trade.EntryPrice;
        int marketTrend = 0;
        if (price > indicators.Ema20) marketTrend++;
        if (indicators.Ema20 > indicators.Ema50) marketTrend++;
        if (indicators.Ema50 > indicators.Ema200) marketTrend++;

        if (price < indicators.Ema20) marketTrend--;
        if (indicators.Ema20 < indicators.Ema50) marketTrend--;
        if (indicators.Ema50 < indicators.Ema200) marketTrend--;

        double rsiMarketBias = indicators.Rsi switch
        {
            >= 65m => 0.0008,
            <= 35m => -0.0008,
            _      => 0.0
        };

        double marketMomentum = marketTrend * 0.0004 + rsiMarketBias;

        // ── 4. Indicator bias — applied to muTrade for win/loss calculation ──
        bool isLong = trade.Direction == TradeDirection.Long;
        int tradeTrendScore = 0;
        if (isLong ? price > indicators.Ema20  : price < indicators.Ema20)  tradeTrendScore++;
        if (isLong ? indicators.Ema20 > indicators.Ema50  : indicators.Ema20 < indicators.Ema50)  tradeTrendScore++;
        if (isLong ? indicators.Ema50 > indicators.Ema200 : indicators.Ema50 < indicators.Ema200) tradeTrendScore++;

        double tradeRsiBias = indicators.Rsi switch
        {
            >= 60m => isLong ?  0.0015 : -0.0010,
            <= 40m => isLong ? -0.0010 :  0.0015,
            _      => 0.0
        };

        double tradeIndicatorBias = (isLong ? 1 : -1) * tradeTrendScore * 0.001 + tradeRsiBias;

        double sigmaFinal  = Math.Clamp(sigmaBlended, 0.005, 0.15);
        double muMarket    = muEma + marketMomentum;                  // pure market + technical momentum
        double muTrade     = muEma + tradeIndicatorBias;              // biased toward trade direction

        return (muMarket, muTrade, sigmaFinal);
    }

    /// <summary>
    /// Samples fat-tailed returns using Student's t-distribution with df degrees of freedom.
    /// Captures black swan shocks and tail risk inherent in crypto markets.
    /// </summary>
    private static double SampleFatTailedNoise(Random rng, double df = 5.0)
    {
        double z = SampleStandardNormal(rng);
        double v1 = SampleStandardNormal(rng);
        double v2 = SampleStandardNormal(rng);
        double v3 = SampleStandardNormal(rng);
        double v4 = SampleStandardNormal(rng);
        double v5 = SampleStandardNormal(rng);
        double chiSquare = v1 * v1 + v2 * v2 + v3 * v3 + v4 * v4 + v5 * v5;

        double tVal = z / Math.Sqrt(Math.Max(1e-6, chiSquare / df));
        return tVal * Math.Sqrt((df - 2.0) / df);
    }

    /// <summary>Box-Muller transform: samples Z ~ N(0,1).</summary>
    private static double SampleStandardNormal(Random rng)
    {
        double u1 = 1.0 - rng.NextDouble();
        double u2 = 1.0 - rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }

    /// <summary>Linear-interpolated percentile from a sorted list.</summary>
    private static double Percentile(List<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        if (sorted.Count == 1) return sorted[0];

        double index = p * (sorted.Count - 1);
        int lo = (int)Math.Floor(index);
        int hi = Math.Min(lo + 1, sorted.Count - 1);
        double frac = index - lo;
        return sorted[lo] * (1 - frac) + sorted[hi] * frac;
    }
    /// <summary>
    /// Maps a Timeframe enum value to the duration of one candle.
    /// Used to compute real-world timestamps for each trajectory step.
    /// </summary>
    private static TimeSpan TimeframeToCandleDuration(Timeframe tf) => tf switch
    {
        Timeframe.M1  => TimeSpan.FromMinutes(1),
        Timeframe.M5  => TimeSpan.FromMinutes(5),
        Timeframe.M15 => TimeSpan.FromMinutes(15),
        Timeframe.M30 => TimeSpan.FromMinutes(30),
        Timeframe.H1  => TimeSpan.FromHours(1),
        Timeframe.H4  => TimeSpan.FromHours(4),
        Timeframe.D1  => TimeSpan.FromDays(1),
        _             => TimeSpan.FromHours(1)
    };
}
