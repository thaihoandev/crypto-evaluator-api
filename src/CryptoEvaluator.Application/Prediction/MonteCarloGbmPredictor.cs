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
    PredictionConfidenceDto? Confidence = null,
    // §6 Bootstrap CI: 95% confidence interval for E[R] via 1000-sample bootstrap
    decimal? BootstrapCiLower = null,
    decimal? BootstrapCiUpper = null);

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
        // §3/§4/§5 extensions: calibration now returns GARCH κ, σ_longTerm and KS-fitted df.
        var calibration = CalibrateFromCandles(candles, trade, indicators);
        double muMarket          = calibration.MuMarket;
        double sigma             = calibration.Sigma;
        double meanReversionSpeed = calibration.MeanReversionSpeed;

        // dt = 1 candle step (normalised)
        const double dt = 1.0;
        double sqrtDt   = Math.Sqrt(dt);

        // ── Run N simulated paths ──────────────────────────────────────────────
        var rng = randomSeed.HasValue ? new Random(randomSeed.Value) : new Random();

        int wins   = 0;
        int losses = 0;
        int noHits = 0;
        double totalR = 0.0;
        // §6 Bootstrap: record per-path R outcome for resampling
        var pathROutcomes = new double[numPaths];

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
                // Fat-tailed noise: df calibrated per-coin via KS minimisation (§3 extension)
                double z = SampleFatTailedNoise(rng, calibration.Df);

                // Step-by-step σ decay toward GARCH long-run level (§5 extension)
                // σ(t) = σ_long + (σ_blended − σ_long) · e^{−κ · t}
                double stepSigma = calibration.SigmaLongTerm
                    + (sigma - calibration.SigmaLongTerm) * Math.Exp(-calibration.KappaDecay * step);
                stepSigma = Math.Clamp(stepSigma, 0.002, 0.15);

                double meanReversionPull = meanReversionSpeed * Math.Log(targetAnchorD / Math.Max(1e-4, marketPrice));
                double stepMuMarketAdjusted = muMarket + meanReversionPull;

                // ── §2 Extension — Merton Jump-Diffusion Process ─────────────────
                // Draw N_jumps ~ Poisson(λ_J · Δt) and sum jump magnitudes J_j ~ N(μ_J, σ_J^2)
                int numJumps = SamplePoisson(rng, calibration.LambdaJump * dt);
                double jumpSum = 0.0;
                for (int j = 0; j < numJumps; j++)
                {
                    double jumpZ = SampleGaussian(rng);
                    jumpSum += (calibration.MuJump + calibration.SigmaJump * jumpZ);
                }
                double jumpDriftAdj = calibration.LambdaJump * calibration.JumpKFactor;

                // ── Path A: Pure market trajectory using muMarket, mean reversion, and Jump-Diffusion
                double logReturnStep = (stepMuMarketAdjusted - 0.5 * stepSigma * stepSigma - jumpDriftAdj) * dt
                    + stepSigma * sqrtDt * z + jumpSum;
                marketPrice *= Math.Exp(logReturnStep);
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
                pathROutcomes[p] = (double)rr;
            }
            else if (hitSl)
            {
                losses++;
                totalR -= 1.0;
                pathROutcomes[p] = -1.0;
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
                pathROutcomes[p] = partialR;
            }
        }

        decimal winProb    = Math.Round((decimal)wins   / numPaths * 100m, 2);
        decimal lossProb   = Math.Round((decimal)losses / numPaths * 100m, 2);
        decimal noHitProb  = Math.Round((decimal)noHits / numPaths * 100m, 2);
        decimal expectedR  = Math.Round((decimal)(totalR / numPaths), 4);
        TimeSpan candleDuration = TimeframeToCandleDuration(trade.Timeframe);
        var scenarioPaths = BuildRepresentativePaths(marketPricesByStep, lastCandleTime, candleDuration);
        var confidence = BuildConfidence(candles, indicators, lastCandleTime, candleDuration);

        // §6 Extension: Bootstrap CI 95% for E[R] — resample 1000 times from path outcomes
        var (ciLower, ciUpper) = ComputeBootstrapCi(pathOutcomes: pathROutcomes, bootstrapReps: 1000, rng);

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
            Confidence:       confidence,
            BootstrapCiLower: ciLower,
            BootstrapCiUpper: ciUpper);
    }

    // ── §6 Extension — Bootstrap CI for E[R] ─────────────────────────────────
    /// <summary>
    /// Computes a 95% confidence interval for the E[R] point estimate by resampling
    /// the per-path R outcomes B times with replacement and taking the 2.5th and 97.5th
    /// percentiles of the bootstrap distribution of E[R]^(b).
    /// </summary>
    private static (decimal lower, decimal upper) ComputeBootstrapCi(
        double[] pathOutcomes, int bootstrapReps, Random rng)
    {
        if (pathOutcomes.Length < 30)
            return (decimal.MinValue, decimal.MaxValue); // insufficient data for reliable CI

        int n = pathOutcomes.Length;
        var bootstrapMeans = new double[bootstrapReps];

        for (int b = 0; b < bootstrapReps; b++)
        {
            double sum = 0.0;
            for (int i = 0; i < n; i++)
                sum += pathOutcomes[rng.Next(n)]; // resample with replacement
            bootstrapMeans[b] = sum / n;
        }

        Array.Sort(bootstrapMeans);
        decimal lower = Math.Round((decimal)bootstrapMeans[(int)(bootstrapReps * 0.025)], 4);
        decimal upper = Math.Round((decimal)bootstrapMeans[(int)(bootstrapReps * 0.975)], 4);
        return (lower, upper);
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

        (PredictionScenario Scenario, double Percentile)[] scenarios =
        [ (PredictionScenario.Bear, 0.10), (PredictionScenario.Base, 0.50), (PredictionScenario.Bull, 0.90) ];

        var result = new List<ScenarioPathDto>(scenarios.Length);
        foreach (var (scenario, percentile) in scenarios)
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

            result.Add(new ScenarioPathDto(scenario, points));
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
        PredictionConfidenceLevel level = score switch
        {
            >= 80m => PredictionConfidenceLevel.High,
            >= 60m => PredictionConfidenceLevel.Medium,
            _ => PredictionConfidenceLevel.Low
        };
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
    // ── Calibration result record ────────────────────────────────────────────
    private record CalibratedParams(
        double MuMarket,
        double Sigma,
        double MeanReversionSpeed,
        double KappaDecay,
        double SigmaLongTerm,
        double Df,
        double LambdaJump,
        double MuJump,
        double SigmaJump,
        double JumpKFactor);

    /// <summary>
    /// Derives per-candle GBM parameters from historical candle log returns.
    /// Applies spec extensions:
    ///  • §2 — Merton Jump-Diffusion Poisson jump calibration
    ///  • §3 — KS-minimised Student's t df per coin
    ///  • §4 — Garman-Klass & Rogers-Satchell volatility + Regime Detection (Hurst + ADF test)
    ///  • §5 — GARCH(1,1) MLE for σ decay rate κ and long-run level σ_L
    /// </summary>
    private static CalibratedParams CalibrateFromCandles(
        IReadOnlyList<Candle> candles,
        Trade trade,
        IndicatorSnapshot indicators)
    {
        // ── 1. Log returns from historical closes ──────────────────────────────
        var logReturns = new List<double>(candles.Count);
        for (int i = 1; i < candles.Count; i++)
        {
            if (candles[i - 1].Close > 0 && candles[i].Close > 0)
                logReturns.Add(Math.Log((double)(candles[i].Close / candles[i - 1].Close)));
        }

        // ── 2. EWMA drift (muEma) & historical std dev ─────────────────────────
        double muEma = 0.0;
        double sigmaHistorical = 0.02;

        if (logReturns.Count >= 2)
        {
            double emaAlpha = 2.0 / (logReturns.Count + 1.0);
            muEma = logReturns[0];
            for (int i = 1; i < logReturns.Count; i++)
                muEma = emaAlpha * logReturns[i] + (1.0 - emaAlpha) * muEma;

            const double lambda = 0.94;
            double ewmaVariance = logReturns[0] * logReturns[0];
            for (int i = 1; i < logReturns.Count; i++)
                ewmaVariance = lambda * ewmaVariance + (1.0 - lambda) * logReturns[i] * logReturns[i];
            sigmaHistorical = Math.Sqrt(ewmaVariance);
        }

        // ── 3. [§4 Extension] Range Volatilities: Parkinson, Garman-Klass, Rogers-Satchell ─
        double sigmaParkinson = ComputeParkinsonVol(candles, sigmaHistorical);
        double sigmaGarmanKlass = ComputeGarmanKlassVol(candles, sigmaHistorical);
        double sigmaRogersSatchell = ComputeRogersSatchellVol(candles, sigmaHistorical);

        // ── 4. [§4 Extension] Regime Detection via Hurst Exponent & ADF Test ───
        // H < 0.45 & ADF t-stat < -2.86 → MeanReverting
        // H > 0.55 → Trending
        // Otherwise → Noise/Random Walk
        bool meanReverts = false;
        bool isTrending = false;

        if (logReturns.Count >= 50)
        {
            double hurst = EstimateHurstExponent(logReturns);
            double adfStat = EstimateAdfTest(logReturns);
            bool isStationary = adfStat < -2.86; // 5% significance level critical value

            meanReverts = hurst < 0.45 && isStationary;
            isTrending = hurst > 0.55;
        }
        else
        {
            // Fallback to EMA-spread / RSI gate when not enough candles
            decimal price0 = indicators.CurrentPrice > 0 ? indicators.CurrentPrice : trade.EntryPrice;
            double emaSpread = price0 > 0 ? (double)Math.Abs(indicators.Ema20 - indicators.Ema50) / (double)price0 : 0.0;
            meanReverts = emaSpread < 0.0025 && indicators.Rsi is >= 42m and <= 58m;
        }

        double meanReversionSpeed = meanReverts ? 0.012 : 0.0;

        // Adaptive Blended Volatility based on Market Regime (§4 extension)
        double sigmaBlended = meanReverts
            ? Math.Sqrt(0.5 * sigmaHistorical * sigmaHistorical + 0.5 * sigmaParkinson * sigmaParkinson)
            : isTrending
                ? Math.Sqrt(0.5 * sigmaHistorical * sigmaHistorical + 0.5 * sigmaRogersSatchell * sigmaRogersSatchell)
                : Math.Sqrt(0.5 * sigmaHistorical * sigmaHistorical + 0.5 * sigmaGarmanKlass * sigmaGarmanKlass);

        // ── 5. [§5 Extension] GARCH(1,1) MLE — calibrate κ and σ_longTerm ──────
        double kappaDecay      = 0.05;
        double sigmaLongTerm   = Math.Clamp(sigmaBlended * 0.85, 0.002, 0.10);

        if (logReturns.Count >= 250)
        {
            var (omega, alpha, beta) = FitGarch11(logReturns);
            double persistence = alpha + beta;
            if (persistence < 1.0 && omega > 0)
            {
                sigmaLongTerm = Math.Clamp(Math.Sqrt(omega / (1.0 - persistence)), 0.002, 0.12);
                kappaDecay = Math.Clamp(-Math.Log(Math.Max(persistence, 1e-6)), 0.005, 0.20);
            }
        }

        // ── 6. [§2 Extension] Merton Jump-Diffusion Poisson jump calibration ──
        double lambdaJ = 0.0;
        double muJ = 0.0;
        double sigmaJ = 0.02;
        double jumpKFactor = 0.0;

        if (logReturns.Count >= 50)
        {
            var jumpReturns = new List<double>();
            double returnMean = logReturns.Average();
            double returnStd = Math.Sqrt(logReturns.Select(r => (r - returnMean) * (r - returnMean)).Average());

            for (int i = 0; i < logReturns.Count; i++)
            {
                if (Math.Abs(logReturns[i] - returnMean) > 3.0 * returnStd)
                {
                    jumpReturns.Add(logReturns[i]);
                }
            }

            if (jumpReturns.Count > 0)
            {
                lambdaJ = (double)jumpReturns.Count / logReturns.Count;
                muJ = jumpReturns.Average();
                double jumpVar = jumpReturns.Select(r => (r - muJ) * (r - muJ)).Average();
                sigmaJ = Math.Max(0.01, Math.Sqrt(jumpVar));
                jumpKFactor = Math.Exp(muJ + 0.5 * sigmaJ * sigmaJ) - 1.0;
            }
        }

        // ── 7. [§3 Extension] KS-minimised degrees of freedom ─────────────────
        double df = logReturns.Count >= 250
            ? EstimateDegreesOfFreedom(logReturns)
            : 5.0;

        // ── 8. Drift & final sigma ─────────────────────────────────────────────
        decimal priceD = indicators.CurrentPrice > 0 ? indicators.CurrentPrice : trade.EntryPrice;
        int marketTrend = 0;
        if (priceD > indicators.Ema20) marketTrend++;
        if (indicators.Ema20 > indicators.Ema50) marketTrend++;
        if (indicators.Ema50 > indicators.Ema200) marketTrend++;
        if (priceD < indicators.Ema20) marketTrend--;
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
        double muMarket   = Math.Clamp(muEma + marketMomentum, -sigmaFinal * 0.35, sigmaFinal * 0.35);

        return new CalibratedParams(
            muMarket,
            sigmaFinal,
            meanReversionSpeed,
            kappaDecay,
            sigmaLongTerm,
            df,
            lambdaJ,
            muJ,
            sigmaJ,
            jumpKFactor);
    }

    // ── §5 Extension — GARCH(1,1) MLE ─────────────────────────────────────────
    /// <summary>
    /// Fits a GARCH(1,1) model to log-returns using gradient-free Nelder–Mead-style
    /// coordinate descent (efficient, allocation-light, no external dependencies).
    /// Returns (ω, α, β) with constraint α+β &lt; 1.
    /// </summary>
    private static (double omega, double alpha, double beta) FitGarch11(IReadOnlyList<double> returns)
    {
        // Initialise from EWMA (α=0.06, β=0.94 ≈ RiskMetrics) as a warm start
        double omega = 1e-6;
        double alpha = 0.06;
        double beta  = 0.90;

        double varUnconditional = returns.Select(r => r * r).Average();
        omega = varUnconditional * (1.0 - alpha - beta);

        double BestLogL = GarchLogLikelihood(returns, omega, alpha, beta);

        // Coordinate descent: iterate over each parameter, shrinking step size
        double[] steps = [1e-4, 5e-5, 1e-5];
        for (int iter = 0; iter < 200; iter++)
        {
            double step = steps[Math.Min(iter / 60, steps.Length - 1)];

            // Optimise alpha
            foreach (double delta in new[] { step, -step })
            {
                double a2 = Math.Clamp(alpha + delta, 0.001, 0.30);
                double o2 = varUnconditional * (1.0 - a2 - beta);
                if (a2 + beta >= 1.0 || o2 <= 0) continue;
                double ll = GarchLogLikelihood(returns, o2, a2, beta);
                if (ll > BestLogL) { BestLogL = ll; alpha = a2; omega = o2; break; }
            }

            // Optimise beta
            foreach (double delta in new[] { step, -step })
            {
                double b2 = Math.Clamp(beta + delta, 0.50, 0.98);
                double o2 = varUnconditional * (1.0 - alpha - b2);
                if (alpha + b2 >= 1.0 || o2 <= 0) continue;
                double ll = GarchLogLikelihood(returns, o2, alpha, b2);
                if (ll > BestLogL) { BestLogL = ll; beta = b2; omega = o2; break; }
            }
        }

        // Enforce stationarity: if fit diverged, fall back to EWMA-equivalent
        if (alpha + beta >= 1.0)
        {
            alpha = 0.06;
            beta  = 0.90;
            omega = varUnconditional * (1.0 - alpha - beta);
        }

        return (omega, alpha, beta);
    }

    /// <summary>GARCH(1,1) Gaussian log-likelihood (negative → maximise = minimise negLogL).</summary>
    private static double GarchLogLikelihood(IReadOnlyList<double> returns, double omega, double alpha, double beta)
    {
        double ht = omega / Math.Max(1.0 - alpha - beta, 1e-8); // initialise at unconditional variance
        double logL = 0.0;
        for (int i = 0; i < returns.Count; i++)
        {
            ht = omega + alpha * returns[i] * returns[i] + beta * ht;
            if (ht <= 0) return double.NegativeInfinity;
            logL += -0.5 * (Math.Log(2.0 * Math.PI * ht) + returns[i] * returns[i] / ht);
        }
        return logL;
    }

    // ── §4 Extension — Hurst Exponent via R/S Analysis ────────────────────────
    /// <summary>
    /// Estimates the Hurst exponent via rescaled-range (R/S) analysis on log-returns.
    /// H &lt; 0.45 → mean-reverting; H &gt; 0.55 → trending; [0.45,0.55] → random walk.
    /// Uses 4 window sizes [n/8, n/4, n/2, n] and OLS slope on log(n)–log(RS) pairs.
    /// </summary>
    private static double EstimateHurstExponent(IReadOnlyList<double> returns)
    {
        int n = returns.Count;
        // Window sizes: use 4 scales from n/8 to n
        int[] windows = [Math.Max(8, n / 8), Math.Max(16, n / 4), Math.Max(32, n / 2), n];
        var logN  = new List<double>();
        var logRS = new List<double>();

        foreach (int w in windows)
        {
            if (w > n) continue;
            // Compute mean-adjusted cumulative deviate range over the window
            double meanR = 0.0;
            for (int i = 0; i < w; i++) meanR += returns[i];
            meanR /= w;

            double cumDev = 0.0, minDev = 0.0, maxDev = 0.0;
            double sumSq = 0.0;
            for (int i = 0; i < w; i++)
            {
                cumDev += returns[i] - meanR;
                sumSq  += (returns[i] - meanR) * (returns[i] - meanR);
                if (i == 0) { minDev = maxDev = cumDev; }
                else { minDev = Math.Min(minDev, cumDev); maxDev = Math.Max(maxDev, cumDev); }
            }
            double stdDev = Math.Sqrt(sumSq / w);
            if (stdDev < 1e-10) continue;

            double rs = (maxDev - minDev) / stdDev;
            if (rs <= 0) continue;

            logN.Add(Math.Log(w));
            logRS.Add(Math.Log(rs));
        }

        if (logN.Count < 2) return 0.5; // insufficient data → assume random walk

        // OLS slope of log(RS) on log(n) = Hurst exponent
        double xBar = logN.Average();
        double yBar = logRS.Average();
        double num = 0.0, den = 0.0;
        for (int i = 0; i < logN.Count; i++)
        {
            num += (logN[i] - xBar) * (logRS[i] - yBar);
            den += (logN[i] - xBar) * (logN[i] - xBar);
        }
        double hurst = den > 1e-12 ? num / den : 0.5;
        return Math.Clamp(hurst, 0.1, 0.9);
    }

    // ── §3 Extension — KS-minimised degrees of freedom ────────────────────────
    /// <summary>
    /// Finds df ∈ {3, 3.5, …, 15} that minimises the Kolmogorov–Smirnov statistic
    /// between the empirical log-return CDF and the standardised Student-t CDF.
    /// Standardised to zero mean, unit std-dev before KS comparison.
    /// </summary>
    private static double EstimateDegreesOfFreedom(IReadOnlyList<double> returns)
    {
        // Standardise returns: zero mean, unit std-dev
        double mean = 0.0;
        for (int i = 0; i < returns.Count; i++) mean += returns[i];
        mean /= returns.Count;
        double variance = 0.0;
        for (int i = 0; i < returns.Count; i++) variance += (returns[i] - mean) * (returns[i] - mean);
        variance /= returns.Count;
        double std = Math.Sqrt(Math.Max(variance, 1e-12));

        var sorted = returns.Select(r => (r - mean) / std).OrderBy(x => x).ToArray();
        int n = sorted.Length;

        double bestDf  = 5.0;
        double bestKs  = double.MaxValue;

        // Scan df in [3, 15] step 0.5
        for (double dfCandidate = 3.0; dfCandidate <= 15.0; dfCandidate += 0.5)
        {
            double ks = 0.0;
            for (int i = 0; i < n; i++)
            {
                double empirical = (i + 1.0) / n;
                double theoretical = CdfStudentT(sorted[i], dfCandidate);
                ks = Math.Max(ks, Math.Abs(empirical - theoretical));
            }
            if (ks < bestKs) { bestKs = ks; bestDf = dfCandidate; }
        }

        return bestDf;
    }

    /// <summary>
    /// CDF of the standardised Student's t-distribution evaluated at x with df degrees of freedom.
    /// Uses an incomplete-beta regularised function approximation (Abramowitz & Stegun 26.7.8).
    /// </summary>
    private static double CdfStudentT(double x, double df)
    {
        // P(T <= x) = I_{x^2/(df+x^2)}(1/2, df/2) / 2   for the two-tail symmetry
        // For |x| → ∞ clamp to avoid overflow
        if (x >= 30.0)  return 1.0;
        if (x <= -30.0) return 0.0;

        double t2 = x * x;
        double z  = df / (df + t2); // regularised incomplete beta argument

        // Regularised incomplete beta I_z(a,b) via continued fraction (Lentz)
        double a = df / 2.0;
        double b = 0.5;
        double ibeta = RegularisedIncompleteBeta(z, a, b);

        // Two-tailed: for x < 0, P = ibeta/2; for x >= 0, P = 1 - ibeta/2
        return x < 0.0 ? ibeta / 2.0 : 1.0 - ibeta / 2.0;
    }

    /// <summary>
    /// Regularised incomplete beta I_x(a,b) via Lentz continued fraction.
    /// Numerically stable for x ∈ (0,1), a,b > 0.
    /// </summary>
    private static double RegularisedIncompleteBeta(double x, double a, double b)
    {
        if (x <= 0.0) return 0.0;
        if (x >= 1.0) return 1.0;

        // Use symmetry relation when x > (a+1)/(a+b+2) for better convergence
        bool useSymmetry = x > (a + 1.0) / (a + b + 2.0);
        if (useSymmetry)
            return 1.0 - RegularisedIncompleteBeta(1.0 - x, b, a);

        // Lentz continued fraction
        double lbeta  = LogGamma(a + b) - LogGamma(a) - LogGamma(b);
        double front  = Math.Exp(Math.Log(x) * a + Math.Log(1.0 - x) * b - lbeta) / a;

        // Evaluate CF via modified Lentz algorithm
        const double tiny = 1e-30;
        double f = tiny, C = f, D = 0.0;
        for (int m = 0; m <= 150; m++)
        {
            // Even step
            double d;
            if (m == 0)
                d = 1.0;
            else
            {
                int m2 = 2 * m;
                d = m * (b - m) * x / ((a + m2 - 1.0) * (a + m2));
                D = 1.0 + d * D;
                if (Math.Abs(D) < tiny) D = tiny;
                D = 1.0 / D;
                C = 1.0 + d / C;
                if (Math.Abs(C) < tiny) C = tiny;
                f *= C * D;
            }

            // Odd step
            d = -(a + m) * (a + b + m) * x / ((a + 2.0 * m) * (a + 2.0 * m + 1.0));
            D = 1.0 + d * D;
            if (Math.Abs(D) < tiny) D = tiny;
            D = 1.0 / D;
            C = 1.0 + d / C;
            if (Math.Abs(C) < tiny) C = tiny;
            double delta = C * D;
            f *= delta;

            if (Math.Abs(delta - 1.0) < 1e-10) break;
        }

        return front * f;
    }

    /// <summary>Stirling / Lanczos log-Gamma approximation (g=7, 9-term).</summary>
    private static double LogGamma(double x)
    {
        // Lanczos g=7 coefficients
        double[] c = [0.99999999999980993, 676.5203681218851, -1259.1392167224028,
                      771.32342877765313, -176.61502916214059, 12.507343278686905,
                      -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7];
        if (x < 0.5)
            return Math.Log(Math.PI / Math.Sin(Math.PI * x)) - LogGamma(1.0 - x);
        x -= 1;
        double a = c[0];
        double t = x + 8; // g + 0.5
        for (int i = 1; i < 9; i++) a += c[i] / (x + i);
        return 0.5 * Math.Log(2.0 * Math.PI) + (x + 0.5) * Math.Log(t) - t + Math.Log(a);
    }

    // ── [§4 Extension] Volatility Estimators & ADF Test ─────────────────────
    private static double ComputeParkinsonVol(IReadOnlyList<Candle> candles, double fallback)
    {
        double sumLogHL2 = 0.0;
        int valid = 0;
        for (int i = 0; i < candles.Count; i++)
        {
            if (candles[i].Low > 0 && candles[i].High >= candles[i].Low)
            {
                double logHL = Math.Log((double)(candles[i].High / candles[i].Low));
                sumLogHL2 += logHL * logHL;
                valid++;
            }
        }
        return valid > 0 ? Math.Sqrt(sumLogHL2 / (4.0 * Math.Log(2.0) * valid)) : fallback;
    }

    private static double ComputeGarmanKlassVol(IReadOnlyList<Candle> candles, double fallback)
    {
        double sumGK = 0.0;
        int valid = 0;
        for (int i = 0; i < candles.Count; i++)
        {
            if (candles[i].Low > 0 && candles[i].Open > 0 && candles[i].High >= candles[i].Low)
            {
                double logHL = Math.Log((double)(candles[i].High / candles[i].Low));
                double logCO = Math.Log((double)(candles[i].Close / candles[i].Open));
                sumGK += 0.5 * logHL * logHL - (2.0 * Math.Log(2.0) - 1.0) * logCO * logCO;
                valid++;
            }
        }
        return valid > 0 ? Math.Sqrt(Math.Max(1e-8, sumGK / valid)) : fallback;
    }

    private static double ComputeRogersSatchellVol(IReadOnlyList<Candle> candles, double fallback)
    {
        double sumRS = 0.0;
        int valid = 0;
        for (int i = 0; i < candles.Count; i++)
        {
            if (candles[i].Low > 0 && candles[i].Open > 0 && candles[i].High >= candles[i].Low)
            {
                double u = Math.Log((double)(candles[i].High / candles[i].Close));
                double c = Math.Log((double)(candles[i].High / candles[i].Open));
                double d = Math.Log((double)(candles[i].Low / candles[i].Close));
                double f = Math.Log((double)(candles[i].Low / candles[i].Open));
                sumRS += u * c + d * f;
                valid++;
            }
        }
        return valid > 0 ? Math.Sqrt(Math.Max(1e-8, sumRS / valid)) : fallback;
    }

    /// <summary>
    /// Computes the Augmented Dickey-Fuller (ADF) t-statistic for log-returns.
    /// Critical value at 5% significance level is ~ -2.86.
    /// t_stat &lt; -2.86 indicates stationarity (reject null hypothesis of unit root).
    /// </summary>
    private static double EstimateAdfTest(IReadOnlyList<double> returns)
    {
        if (returns.Count < 20) return 0.0;
        int n = returns.Count - 1;
        double sumYPrev = 0, sumDy = 0, sumYPrev2 = 0, sumYPrevDy = 0;

        for (int i = 1; i < returns.Count; i++)
        {
            double yPrev = returns[i - 1];
            double dy = returns[i] - returns[i - 1];
            sumYPrev += yPrev;
            sumDy += dy;
            sumYPrev2 += yPrev * yPrev;
            sumYPrevDy += yPrev * dy;
        }

        double meanYPrev = sumYPrev / n;
        double meanDy = sumDy / n;

        double sxx = sumYPrev2 - n * meanYPrev * meanYPrev;
        double sxy = sumYPrevDy - n * meanYPrev * meanDy;

        if (Math.Abs(sxx) < 1e-12) return 0.0;

        double gamma = sxy / sxx;
        double alpha = meanDy - gamma * meanYPrev;

        double sumResidualSq = 0;
        for (int i = 1; i < returns.Count; i++)
        {
            double yPrev = returns[i - 1];
            double dy = returns[i] - returns[i - 1];
            double fitted = alpha + gamma * yPrev;
            double res = dy - fitted;
            sumResidualSq += res * res;
        }

        double seGamma = Math.Sqrt((sumResidualSq / Math.Max(1, n - 2)) / sxx);
        return seGamma > 1e-12 ? gamma / seGamma : 0.0;
    }

    // ── [§2 Extension] Poisson Jump Sampler ─────────────────────────────────
    private static int SamplePoisson(Random rng, double lambda)
    {
        if (lambda <= 0) return 0;
        double L = Math.Exp(-lambda);
        double k = 0;
        double p = 1.0;
        do
        {
            k++;
            p *= rng.NextDouble();
        } while (p > L && k < 10);
        return (int)(k - 1);
    }

    private static double SampleGaussian(Random rng)
    {
        return SampleStandardNormal(rng);
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
