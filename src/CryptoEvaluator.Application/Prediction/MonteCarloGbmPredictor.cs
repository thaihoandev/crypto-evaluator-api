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
    IReadOnlyList<TrajectoryPointDto>? TrajectoryPoints = null,
    IReadOnlyList<ScenarioPathDto>? ScenarioPaths = null,
    PredictionConfidenceDto? Confidence = null);

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

        if (candles.Count < 2)
            throw new ArgumentException("At least two closed candles are required for prediction.", nameof(candles));

        // Anchor the forecast to the latest observed close, not a possibly stale
        // entry price. Mean reversion is enabled only for a detected ranging regime.
        var (muMarket, sigma, meanReversionSpeed) = CalibrateFromCandles(candles, trade, indicators);

        // dt = 1 candle step (normalised)
        const double dt = 1.0;
        double sqrtDt   = Math.Sqrt(dt);

        // ── Run N simulated paths ──────────────────────────────────────────────
        var rng = randomSeed.HasValue ? new Random(randomSeed.Value) : new Random();

        int wins   = 0;
        int losses = 0;
        int noHits = 0;
        double totalR = 0.0;

        // The same price path drives both chart output and TP/SL evaluation.
        var marketPricesByStep = new List<double>[ProjectionSteps + 1];
        for (int i = 0; i <= ProjectionSteps; i++)
            marketPricesByStep[i] = new List<double>(numPaths);

        int[] tpHitsByStep = new int[ProjectionSteps + 1];
        int[] slHitsByStep = new int[ProjectionSteps + 1];

        double entryD = (double)entry;
        double anchorPriceD = (double)candles[^1].Close;
        double tpD    = (double)tp;
        double slD    = (double)sl;
        double targetAnchorD = (double)(indicators.Ema20 > 0 && indicators.Ema50 > 0
            ? (indicators.Ema20 + indicators.Ema50) / 2m
            : indicators.CurrentPrice > 0 ? indicators.CurrentPrice : trade.EntryPrice);

        for (int p = 0; p < numPaths; p++)
        {
            double marketPrice = anchorPriceD;
            marketPricesByStep[0].Add(marketPrice);

            bool hitTp = false;
            bool hitSl = false;
            bool terminated = false;
            int tpStepHit = -1;
            int slStepHit = -1;

            for (int step = 1; step <= ProjectionSteps; step++)
            {
                // Fat-tailed noise sampling: Student's t-distribution with df=5
                double z = SampleFatTailedNoise(rng, df: 5.0);

                // Persistent EWMA-calibrated volatility avoids paths collapsing
                // into a flat median as the horizon increases.
                double stepSigma = sigma;
                double meanReversionPull = meanReversionSpeed * Math.Log(targetAnchorD / Math.Max(1e-4, marketPrice));
                double stepMuMarketAdjusted = muMarket + meanReversionPull;

                // ── Path A: Pure market trajectory using muMarket & mean reversion
                marketPrice *= Math.Exp((stepMuMarketAdjusted - 0.5 * stepSigma * stepSigma) * dt + stepSigma * sqrtDt * z);
                marketPricesByStep[step].Add(marketPrice);

                // Check TP / SL — only count outcome once per path
                if (!terminated)
                {
                    bool tpHit = isLong ? marketPrice >= tpD : marketPrice <= tpD;
                    bool slHit = isLong ? marketPrice <= slD : marketPrice >= slD;

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
                double finalPrice = marketPrice;
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
        TimeSpan candleDuration = TimeframeToCandleDuration(trade.Timeframe);
        var scenarioPaths = BuildRepresentativePaths(marketPricesByStep, lastCandleTime, candleDuration);
        var confidence = BuildConfidence(candles, indicators, lastCandleTime, candleDuration);

        // ── Build trajectory from raw market percentile envelope ──────────────
        // Uses marketPricesByStep (unclamped) so the displayed path reflects
        // natural market movement, independent of the trade's TP/SL boundaries.
        // Each step maps to srcStep actual candles forward from lastCandleTime.
        var trajectoryPoints = new List<TrajectoryPointDto>(TrajectoryResolution + 1);
        double stepSize = (double)ProjectionSteps / TrajectoryResolution;

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
            TrajectoryPoints: trajectoryPoints,
            ScenarioPaths:    scenarioPaths,
            Confidence:       confidence);
    }

    private static IReadOnlyList<ScenarioPathDto> BuildRepresentativePaths(
        IReadOnlyList<List<double>> pricesByStep,
        DateTime lastCandleTime,
        TimeSpan candleDuration)
    {
        var finalPrices = pricesByStep[^1];
        var ordered = finalPrices
            .Select((price, index) => (price, index))
            .OrderBy(x => x.price)
            .ToList();

        (string Name, double Percentile)[] scenarios =
        [ ("Bear", 0.10), ("Base", 0.50), ("Bull", 0.90) ];

        var result = new List<ScenarioPathDto>(scenarios.Length);
        foreach (var (name, percentile) in scenarios)
        {
            int rank = (int)Math.Round(percentile * (ordered.Count - 1));
            int pathIndex = ordered[rank].index;
            var points = new List<TrajectoryPointDto>(TrajectoryResolution + 1);

            for (int t = 0; t <= TrajectoryResolution; t++)
            {
                int sourceStep = Math.Clamp((int)Math.Round(t * (double)ProjectionSteps / TrajectoryResolution), 0, ProjectionSteps);
                decimal close = (decimal)pricesByStep[sourceStep][pathIndex];
                decimal open = sourceStep == 0 ? close : (decimal)pricesByStep[sourceStep - 1][pathIndex];
                decimal high = Math.Max(open, close);
                decimal low = Math.Min(open, close);

                points.Add(new TrajectoryPointDto(
                    Step: t,
                    Price: RoundPrice(close),
                    UpperBound: RoundPrice(high),
                    LowerBound: RoundPrice(low),
                    Timestamp: lastCandleTime + candleDuration * sourceStep,
                    ExpectedOpen: RoundPrice(open),
                    ExpectedHigh: RoundPrice(high),
                    ExpectedLow: RoundPrice(low),
                    ExpectedClose: RoundPrice(close)));
            }

            result.Add(new ScenarioPathDto(name, points));
        }

        return result;
    }

    private static PredictionConfidenceDto BuildConfidence(
        IReadOnlyList<Candle> candles,
        IndicatorSnapshot indicators,
        DateTime lastCandleTime,
        TimeSpan candleDuration)
    {
        var factors = new List<string>();
        decimal score = Math.Min(35m, candles.Count / 200m * 35m);
        factors.Add($"{candles.Count} closed candles available");

        double ageInCandles = Math.Max(0, (DateTime.UtcNow - (lastCandleTime + candleDuration)).TotalSeconds / candleDuration.TotalSeconds);
        if (ageInCandles <= 2)
        {
            score += 25m;
            factors.Add("market data is recent");
        }
        else
        {
            factors.Add($"market data is {ageInCandles:F1} candles old");
        }

        decimal relativeAtr = indicators.CurrentPrice > 0 ? indicators.Atr / indicators.CurrentPrice : 0m;
        if (relativeAtr is >= 0.001m and <= 0.10m)
        {
            score += 20m;
            factors.Add("ATR is within the calibrated operating range");
        }
        else
        {
            factors.Add("ATR is outside the calibrated operating range");
        }

        bool bullishAlignment = indicators.CurrentPrice >= indicators.Ema20 && indicators.Ema20 >= indicators.Ema50;
        bool bearishAlignment = indicators.CurrentPrice <= indicators.Ema20 && indicators.Ema20 <= indicators.Ema50;
        if (bullishAlignment || bearishAlignment)
        {
            score += 20m;
            factors.Add("EMA trend alignment is clear");
        }
        else
        {
            score += 10m;
            factors.Add("EMA trend alignment is mixed");
        }

        score = Math.Round(Math.Clamp(score, 0m, 100m), 1);
        string level = score switch { >= 80m => "High", >= 60m => "Medium", _ => "Low" };
        return new PredictionConfidenceDto(score, level, factors);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Derives per-candle GBM parameters from historical candle log returns.
    /// Returns three values:
    ///   muMarket : pure historical drift (no direction bias) — for trajectory display
    ///   muTrade  : historical drift + small indicator-alignment bias — for TP/SL hit rate
    ///   sigma    : sample std-dev of log returns — used by both
    /// </summary>
    private static (double muMarket, double sigma, double meanReversionSpeed) CalibrateFromCandles(
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

            // EWMA adapts to the latest volatility regime without mechanically
            // shrinking every simulated candle as the forecast advances.
            const double lambda = 0.94;
            double ewmaVariance = logReturns[0] * logReturns[0];
            for (int i = 1; i < logReturns.Count; i++)
                ewmaVariance = lambda * ewmaVariance + (1.0 - lambda) * logReturns[i] * logReturns[i];
            sigmaHistorical = Math.Sqrt(ewmaVariance);
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

        double sigmaFinal = Math.Clamp(sigmaBlended, 0.002, 0.15);
        double muMarket = Math.Clamp(muEma + marketMomentum, -sigmaFinal * 0.35, sigmaFinal * 0.35);

        double emaSpread = price > 0 ? (double)Math.Abs(indicators.Ema20 - indicators.Ema50) / (double)price : 0.0;
        bool ranging = emaSpread < 0.0025 && indicators.Rsi is >= 42m and <= 58m;
        double meanReversionSpeed = ranging ? 0.012 : 0.0;

        return (muMarket, sigmaFinal, meanReversionSpeed);
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

    private static decimal RoundPrice(decimal price)
    {
        if (price == 0m) return 0m;

        int decimals = price switch
        {
            >= 1_000m => 2,
            >= 1m => 4,
            _ => Math.Clamp(6 - (int)Math.Floor(Math.Log10((double)Math.Abs(price))), 6, 10)
        };
        return Math.Round(price, decimals);
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
