using CryptoEvaluator.Domain.Entities;
using CryptoEvaluator.Domain.Enums;
using CryptoEvaluator.Domain.Models;

namespace CryptoEvaluator.Application.Scoring;

public record TradeScoreResult(
    decimal TotalScore,
    IReadOnlyList<ScoreComponent> Components);

public interface ITradeScoreCalculator
{
    TradeScoreResult Calculate(Trade trade, IndicatorSnapshot indicators, RiskResult risk);
}

/// <summary>
/// Evaluates a trade setup across 8 weighted dimensions.
/// Each dimension produces a <see cref="ScoreComponent"/> that includes a
/// <c>SuggestedAction</c> string built from real indicator values so the
/// frontend can render it verbatim — no client-side inference needed.
/// </summary>
public class TradeScoreCalculator : ITradeScoreCalculator
{
    public TradeScoreResult Calculate(Trade trade, IndicatorSnapshot indicators, RiskResult risk)
    {
        var components = new List<ScoreComponent>
        {
            CalculateTrendScore(trade, indicators),
            CalculateEntryScore(trade, indicators),
            CalculateMomentumScore(trade, indicators),
            CalculateVolumeScore(trade, indicators),
            CalculateSupportResistanceScore(trade, indicators),
            CalculateRiskRewardScore(trade, risk, indicators),
            CalculateVolatilityScore(trade, indicators, risk),
            CalculateMarketContextScore(trade, indicators)
        };

        decimal totalScore = components.Sum(c => c.Score * c.Weight);

        return new TradeScoreResult(Math.Round(totalScore, 2), components);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 1. TREND ALIGNMENT  (weight 20%)
    //    Checks how many of 3 EMA-ordering conditions are satisfied:
    //      Long:  Price > EMA20 > EMA50 > EMA200
    //      Short: Price < EMA20 < EMA50 < EMA200
    // ──────────────────────────────────────────────────────────────────────────
    private static ScoreComponent CalculateTrendScore(Trade trade, IndicatorSnapshot ind)
    {
        bool isLong = trade.Direction == TradeDirection.Long;

        bool condition1 = isLong ? ind.CurrentPrice > ind.Ema20 : ind.CurrentPrice < ind.Ema20;
        bool condition2 = isLong ? ind.Ema20 > ind.Ema50        : ind.Ema20 < ind.Ema50;
        bool condition3 = isLong ? ind.Ema50 > ind.Ema200       : ind.Ema50 < ind.Ema200;

        int count = (condition1 ? 1 : 0) + (condition2 ? 1 : 0) + (condition3 ? 1 : 0);

        decimal score = count switch
        {
            3 => 100m,
            2 => 75m,
            1 => 40m,
            _ => 10m
        };

        string direction = isLong ? "Long" : "Short";
        string exp = $"{direction} trend alignment satisfied {count}/3 EMA conditions " +
                     $"(Price {(isLong ? ">" : "<")} EMA20 [{(condition1 ? "✓" : "✗")}], " +
                     $"EMA20 {(isLong ? ">" : "<")} EMA50 [{(condition2 ? "✓" : "✗")}], " +
                     $"EMA50 {(isLong ? ">" : "<")} EMA200 [{(condition3 ? "✓" : "✗")}]). " +
                     $"Current: Price={ind.CurrentPrice:F2}, EMA20={ind.Ema20:F2}, EMA50={ind.Ema50:F2}, EMA200={ind.Ema200:F2}.";

        string suggestion = BuildTrendSuggestion(isLong, score, count, condition1, condition2, condition3, ind);

        return new ScoreComponent(ScoreCategory.Trend, score, 0.20m, exp, suggestion);
    }

    private static string BuildTrendSuggestion(
        bool isLong, decimal score, int count,
        bool c1, bool c2, bool c3, IndicatorSnapshot ind)
    {
        if (score >= 75)
            return $"Trend is fully aligned. All 3 EMA ordering conditions are satisfied for your " +
                   $"{(isLong ? "Long" : "Short")} bias — no adjustment needed. " +
                   $"Maintain your position direction.";

        if (score >= 40)
        {
            // Identify which conditions are failing and give concrete targets
            var missing = new List<string>();
            if (!c1) missing.Add(isLong
                ? $"Price needs to close above EMA20 ({ind.Ema20:F2}) on this candle"
                : $"Price needs to close below EMA20 ({ind.Ema20:F2}) on this candle");
            if (!c2) missing.Add(isLong
                ? $"EMA20 ({ind.Ema20:F2}) must cross above EMA50 ({ind.Ema50:F2})"
                : $"EMA20 ({ind.Ema20:F2}) must cross below EMA50 ({ind.Ema50:F2})");
            if (!c3) missing.Add(isLong
                ? $"EMA50 ({ind.Ema50:F2}) must stay above EMA200 ({ind.Ema200:F2})"
                : $"EMA50 ({ind.Ema50:F2}) must stay below EMA200 ({ind.Ema200:F2})");

            return $"Partial trend alignment ({count}/3). To strengthen conviction, wait for: " +
                   string.Join("; ", missing) + ". " +
                   $"Consider reducing position size by ~30% until all conditions align.";
        }

        // score = 10 — no conditions met, counter-trend
        return $"Trend is strongly against your {(isLong ? "Long" : "Short")} setup ({count}/3 conditions met). " +
               $"The EMA stack signals a {(isLong ? "bearish" : "bullish")} market. " +
               (isLong
                ? $"Wait for price to reclaim EMA20 ({ind.Ema20:F2}) and EMA20 to cross above EMA50 ({ind.Ema50:F2}) before entering. Alternatively, consider switching to Short."
                : $"Wait for price to break below EMA20 ({ind.Ema20:F2}) and EMA20 to cross below EMA50 ({ind.Ema50:F2}) before entering. Alternatively, consider switching to Long.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 2. ENTRY DISTANCE TO EMA20  (weight 15%)
    //    Measures how many ATRs the entry is from EMA20.
    //    The closer to EMA20, the better risk-calibrated the entry.
    // ──────────────────────────────────────────────────────────────────────────
    private static ScoreComponent CalculateEntryScore(Trade trade, IndicatorSnapshot ind)
    {
        if (ind.Atr <= 0)
            return new ScoreComponent(ScoreCategory.Entry, 50m, 0.15m,
                "ATR unavailable for entry distance check.",
                "Unable to assess entry quality — ATR data missing. Verify market data feed.");

        decimal distToEma20 = Math.Abs(trade.EntryPrice - ind.Ema20) / ind.Atr;

        decimal score = distToEma20 switch
        {
            < 0.5m => 95m,
            < 1.0m => 80m,
            < 2.0m => 60m,
            _      => 35m
        };

        string exp = $"Entry price ({trade.EntryPrice:F2}) is {distToEma20:F2}× ATR ({ind.Atr:F2}) " +
                     $"away from EMA20 ({ind.Ema20:F2}). " +
                     $"Optimal zone: < 0.5× ATR from EMA20 (95 pts). " +
                     $"Current rating: {score} pts.";

        string suggestion = BuildEntrySuggestion(trade, ind, distToEma20, score);

        return new ScoreComponent(ScoreCategory.Entry, score, 0.15m, exp, suggestion);
    }

    private static string BuildEntrySuggestion(Trade trade, IndicatorSnapshot ind, decimal distAtr, decimal score)
    {
        bool isLong = trade.Direction == TradeDirection.Long;

        if (score >= 80)
            return $"Entry is well-positioned — only {distAtr:F2}× ATR from EMA20 ({ind.Ema20:F2}). " +
                   $"This is near the optimal entry zone. Proceed with planned position size.";

        if (score >= 60)
        {
            // Calculate the price at 0.8× ATR from EMA20 as a better entry target
            decimal betterEntry = isLong
                ? ind.Ema20 + ind.Atr * 0.8m
                : ind.Ema20 - ind.Atr * 0.8m;
            return $"Entry is {distAtr:F2}× ATR from EMA20 — slightly extended. " +
                   $"A better entry near {betterEntry:F2} (≤ 1.0× ATR from EMA20) would improve this score. " +
                   $"Consider scaling in: use 50% position now, add the rest on a pullback toward EMA20.";
        }

        // score = 35 — entry chasing
        decimal optimalEntry = isLong
            ? ind.Ema20 + ind.Atr * 0.4m
            : ind.Ema20 - ind.Atr * 0.4m;
        return $"Entry is {distAtr:F2}× ATR from EMA20 — significantly extended. High risk of " +
               $"{(isLong ? "buying a local top" : "selling a local bottom")}. " +
               $"Wait for a retracement toward {optimalEntry:F2} (< 0.5× ATR from EMA20). " +
               $"Entering now risks an adverse move equal to {distAtr - 0.5m:F2}× ATR before any recovery.";
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 3. RSI MOMENTUM  (weight 15%)
    //    Long ideal:  RSI 50–70 (bullish momentum, not overheated)
    //    Short ideal: RSI 30–50 (bearish momentum, not oversold)
    // ──────────────────────────────────────────────────────────────────────────
    private static ScoreComponent CalculateMomentumScore(Trade trade, IndicatorSnapshot ind)
    {
        bool isLong = trade.Direction == TradeDirection.Long;
        decimal rsi = ind.Rsi;
        decimal score;
        string exp;

        if (isLong)
        {
            (score, exp) = rsi switch
            {
                >= 50m and <= 70m => (100m, $"RSI at {rsi:F1} — healthy bullish momentum. Ideal entry window."),
                >= 40m and < 50m  => (70m,  $"RSI at {rsi:F1} — moderate bullish zone. Momentum building but not yet strong."),
                > 70m and <= 80m  => (55m,  $"RSI at {rsi:F1} — overheated bullish momentum. Pullback risk elevated."),
                > 80m             => (30m,  $"RSI at {rsi:F1} — severely overbought. Mean-reversion risk is high."),
                _                 => (40m,  $"RSI at {rsi:F1} — weak momentum. Insufficient bullish force to sustain a Long.")
            };
        }
        else
        {
            (score, exp) = rsi switch
            {
                >= 30m and <= 50m => (100m, $"RSI at {rsi:F1} — healthy bearish momentum. Ideal Short entry window."),
                > 50m and <= 60m  => (55m,  $"RSI at {rsi:F1} — moderate bearish zone. Bearish pressure present but weakening."),
                < 30m             => (30m,  $"RSI at {rsi:F1} — severely oversold. Bounce risk is high for a Short entry."),
                _                 => (40m,  $"RSI at {rsi:F1} — weak bearish momentum. Insufficient pressure to sustain a Short.")
            };
        }

        string suggestion = BuildMomentumSuggestion(isLong, rsi, score);

        return new ScoreComponent(ScoreCategory.Momentum, score, 0.15m, exp, suggestion);
    }

    private static string BuildMomentumSuggestion(bool isLong, decimal rsi, decimal score)
    {
        if (score >= 75)
            return $"RSI at {rsi:F1} is in the ideal zone for {(isLong ? "Long" : "Short")} entries. " +
                   $"Momentum is {(isLong ? "bullish" : "bearish")} without being overextended — proceed with confidence.";

        if (score >= 50)
        {
            if (isLong && rsi > 70m)
                return $"RSI at {rsi:F1} signals overheated buying. Wait for RSI to pull back below 65 " +
                       $"before entering to reduce reversal risk. A {(rsi - 65m):F1}-point RSI cooldown is needed.";
            if (!isLong && rsi < 35m)
                return $"RSI at {rsi:F1} signals oversold conditions — high bounce risk for Short entries. " +
                       $"Wait for RSI to recover above 35 ({(35m - rsi):F1} points) before shorting.";
            return $"RSI at {rsi:F1} is in a neutral zone for {(isLong ? "Long" : "Short")} entries. " +
                   $"Wait for RSI to move {(isLong ? "above 55" : "below 45")} for stronger confirmation. " +
                   $"Consider a smaller initial position (50%) until momentum confirms.";
        }

        // Low score — dangerous entry
        if (isLong && rsi > 80m)
            return $"RSI at {rsi:F1} is critically overbought. Do NOT enter Long now — reversal probability is very high. " +
                   $"RSI needs to drop at least {(rsi - 65m):F1} points (below 65) before the Long becomes viable.";

        if (!isLong && rsi < 30m)
            return $"RSI at {rsi:F1} is critically oversold. Do NOT enter Short now — a relief bounce is likely imminent. " +
                   $"Wait for RSI to stabilize above 35 ({(35m - rsi):F1} points) before shorting.";

        return isLong
            ? $"RSI at {rsi:F1} shows weak bullish momentum. Look for RSI to break above 50 first to confirm a bullish shift."
            : $"RSI at {rsi:F1} shows weak bearish momentum. Look for RSI to break below 50 first to confirm a bearish shift.";
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 4. VOLUME CONFIRMATION  (weight 10%)
    //    VolumeRatio = current volume / 20-period average volume
    //    High ratio = strong participation = better confirmation
    // ──────────────────────────────────────────────────────────────────────────
    private static ScoreComponent CalculateVolumeScore(Trade trade, IndicatorSnapshot ind)
    {
        decimal ratio = ind.VolumeRatio;

        decimal score = ratio switch
        {
            < 0.5m  => 20m,
            < 0.8m  => 50m,
            < 1.2m  => 70m,
            < 2.0m  => 90m,
            _       => 100m
        };

        string exp = $"Volume ratio is {ratio:F2}× the 20-period average " +
                     $"(current: {ind.Volume:F0}, average: {ind.AverageVolume:F0}). " +
                     $"Interpretation: {ratio switch { < 0.5m => "Very low — strong disinterest", < 0.8m => "Below average — weak confirmation", < 1.2m => "Average — neutral", < 2.0m => "Above average — good confirmation", _ => "High — strong participation" }}.";

        string suggestion = BuildVolumeSuggestion(trade, ind, ratio, score);

        return new ScoreComponent(ScoreCategory.Volume, score, 0.10m, exp, suggestion);
    }

    private static string BuildVolumeSuggestion(Trade trade, IndicatorSnapshot ind, decimal ratio, decimal score)
    {
        bool isLong = trade.Direction == TradeDirection.Long;
        decimal targetVolume = ind.AverageVolume * 1.5m;

        if (score >= 75)
            return $"Volume is confirming the move at {ratio:F2}× average ({ind.Volume:F0} vs avg {ind.AverageVolume:F0}). " +
                   $"Strong participation supports this {(isLong ? "Long" : "Short")} — maintain your full planned position size.";

        if (score >= 50)
            return $"Volume at {ratio:F2}× average is somewhat neutral. " +
                   $"Watch for a volume spike above {targetVolume:F0} (1.5× average) to confirm the move. " +
                   $"Until then, consider entering at 60–70% of planned size and adding on volume confirmation.";

        // Low volume — risky entry
        return $"Volume is very low at {ratio:F2}× average ({ind.Volume:F0} vs avg {ind.AverageVolume:F0}). " +
               $"Low participation means price moves can easily reverse. " +
               $"Wait for volume to spike above {targetVolume:F0} ({1.5m / ratio:F1}× current level) before entering, " +
               $"or reduce position size to ≤ 40% of planned.";
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 5. SUPPORT / RESISTANCE  (weight 15%)
    //    Evaluates whether the entry is backed by a key dynamic level (EMA50/EMA200)
    //    and how far price is from those levels.
    // ──────────────────────────────────────────────────────────────────────────
    private static ScoreComponent CalculateSupportResistanceScore(Trade trade, IndicatorSnapshot ind)
    {
        bool isLong = trade.Direction == TradeDirection.Long;

        // Calculate distance from EMA50 and EMA200 in ATR units
        decimal distEma50  = ind.Atr > 0 ? Math.Abs(trade.EntryPrice - ind.Ema50)  / ind.Atr : 999m;
        decimal distEma200 = ind.Atr > 0 ? Math.Abs(trade.EntryPrice - ind.Ema200) / ind.Atr : 999m;

        decimal score;
        string exp;

        if (isLong)
        {
            bool aboveEma50  = trade.EntryPrice >= ind.Ema50;
            bool aboveEma200 = trade.EntryPrice >= ind.Ema200;

            if (aboveEma50 && aboveEma200)
            {
                score = distEma50 <= 1.5m ? 90m : 75m; // Bonus if close to EMA50 as support
                exp   = $"Entry ({trade.EntryPrice:F2}) is above both EMA50 ({ind.Ema50:F2}) and EMA200 ({ind.Ema200:F2}). " +
                        $"Both dynamic levels act as support — strong structural backing. " +
                        $"Distance to EMA50: {distEma50:F2}× ATR.";
            }
            else if (aboveEma200)
            {
                score = 65m;
                exp   = $"Entry ({trade.EntryPrice:F2}) is above EMA200 ({ind.Ema200:F2}) but below EMA50 ({ind.Ema50:F2}). " +
                        $"Macro support present (EMA200) but EMA50 acts as overhead resistance. " +
                        $"Distance to EMA50 resistance: {distEma50:F2}× ATR.";
            }
            else
            {
                score = 35m;
                exp   = $"Entry ({trade.EntryPrice:F2}) is below both EMA50 ({ind.Ema50:F2}) and EMA200 ({ind.Ema200:F2}). " +
                        $"Both major dynamic levels are overhead resistance for this Long. " +
                        $"Distance to EMA200 resistance: {distEma200:F2}× ATR.";
            }
        }
        else
        {
            bool belowEma50  = trade.EntryPrice <= ind.Ema50;
            bool belowEma200 = trade.EntryPrice <= ind.Ema200;

            if (belowEma50 && belowEma200)
            {
                score = distEma50 <= 1.5m ? 90m : 75m;
                exp   = $"Entry ({trade.EntryPrice:F2}) is below both EMA50 ({ind.Ema50:F2}) and EMA200 ({ind.Ema200:F2}). " +
                        $"Both dynamic levels act as resistance — strong structural backing for Short. " +
                        $"Distance to EMA50: {distEma50:F2}× ATR.";
            }
            else if (belowEma200)
            {
                score = 65m;
                exp   = $"Entry ({trade.EntryPrice:F2}) is below EMA200 ({ind.Ema200:F2}) but above EMA50 ({ind.Ema50:F2}). " +
                        $"Macro resistance present (EMA200) but EMA50 acts as underlying support. " +
                        $"Distance to EMA50 support: {distEma50:F2}× ATR.";
            }
            else
            {
                score = 35m;
                exp   = $"Entry ({trade.EntryPrice:F2}) is above both EMA50 ({ind.Ema50:F2}) and EMA200 ({ind.Ema200:F2}). " +
                        $"Both major dynamic levels are below as support for longs. " +
                        $"Shorting into these levels is high risk. Distance to EMA50: {distEma50:F2}× ATR.";
            }
        }

        string suggestion = BuildSupportResistanceSuggestion(trade, ind, score, distEma50, distEma200);

        return new ScoreComponent(ScoreCategory.SupportResistance, score, 0.15m, exp, suggestion);
    }

    private static string BuildSupportResistanceSuggestion(
        Trade trade, IndicatorSnapshot ind, decimal score,
        decimal distEma50, decimal distEma200)
    {
        bool isLong = trade.Direction == TradeDirection.Long;

        if (score >= 75)
            return $"Entry has strong dynamic {(isLong ? "support" : "resistance")} from EMA50 ({ind.Ema50:F2}) and EMA200 ({ind.Ema200:F2}). " +
                   $"Place stop-loss just below EMA50 ({(isLong ? ind.Ema50 - ind.Atr * 0.3m : ind.Ema50 + ind.Atr * 0.3m):F2}) for a structurally sound exit. " +
                   $"No level adjustment needed.";

        if (score >= 60)
            return $"Entry has partial dynamic level support. EMA50 ({ind.Ema50:F2}) is {distEma50:F2}× ATR away — " +
                   $"{(isLong ? "acts as support but EMA50 resistance may slow the move" : "acts as resistance but EMA50 support may limit downside")}. " +
                   $"Consider waiting for price to test and hold off {(isLong ? $"EMA50 ({ind.Ema50:F2})" : $"EMA50 ({ind.Ema50:F2})")} to get a better confirmed entry.";

        // Low score — entering against key levels
        return isLong
            ? $"Entry is below both EMA50 ({ind.Ema50:F2}) and EMA200 ({ind.Ema200:F2}) — both are overhead resistance. " +
              $"The nearest resistance (EMA50) is {distEma50:F2}× ATR away at {ind.Ema50:F2}. " +
              $"Avoid entering Long until price reclaims EMA50 with a confirmed candle close above it."
            : $"Entry is above both EMA50 ({ind.Ema50:F2}) and EMA200 ({ind.Ema200:F2}) — both are strong support below. " +
              $"The nearest support (EMA50) is {distEma50:F2}× ATR away at {ind.Ema50:F2}. " +
              $"Avoid entering Short until price breaks decisively below EMA50.";
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 6. RISK : REWARD RATIO  (weight 15%)
    //    Evaluates whether the potential reward justifies the risk taken.
    //    Minimum viable R:R = 1.5:1. Optimal = 2:1 or higher.
    // ──────────────────────────────────────────────────────────────────────────
    private static ScoreComponent CalculateRiskRewardScore(Trade trade, RiskResult risk, IndicatorSnapshot ind)
    {
        decimal rr = risk.RiskRewardRatio;

        decimal score = rr switch
        {
            < 1.0m  => 20m,
            < 1.5m  => 50m,
            < 2.0m  => 70m,
            < 3.0m  => 90m,
            _       => 100m
        };

        string exp = $"Risk-to-Reward ratio is 1:{rr:F2}. " +
                     $"Entry: {trade.EntryPrice:F2}, Stop Loss: {trade.StopLoss:F2} " +
                     $"(risk {risk.StopLossDistance:F2} = {risk.RiskAmount:F2} USDT), " +
                     $"Take Profit: {trade.TakeProfit:F2} " +
                     $"(reward {risk.TakeProfitDistance:F2}). " +
                     $"Minimum recommended R:R: 1.5:1. Optimal: 2.0:1+.";

        string suggestion = BuildRiskRewardSuggestion(trade, risk, ind, rr, score);

        return new ScoreComponent(ScoreCategory.RiskReward, score, 0.15m, exp, suggestion);
    }

    private static string BuildRiskRewardSuggestion(Trade trade, RiskResult risk, IndicatorSnapshot ind, decimal rr, decimal score)
    {
        bool isLong = trade.Direction == TradeDirection.Long;

        if (score >= 75)
            return $"R:R of 1:{rr:F2} is favorable. With a minimum 40% win rate, this setup is " +
                   $"statistically profitable over a series of trades. Maintain your current " +
                   $"TP ({trade.TakeProfit:F2}) and SL ({trade.StopLoss:F2}) levels.";

        if (score >= 50)
        {
            // Calculate TP needed for 2:1 R:R
            decimal targetTp = isLong
                ? trade.EntryPrice + risk.StopLossDistance * 2m
                : trade.EntryPrice - risk.StopLossDistance * 2m;
            return $"R:R of 1:{rr:F2} is below the 2:1 optimal threshold. " +
                   $"To achieve a 2:1 R:R, move TP to {targetTp:F2} " +
                   $"(current TP: {trade.TakeProfit:F2}, gap: {Math.Abs(targetTp - trade.TakeProfit):F2}). " +
                   $"Alternatively, tighten SL to {(isLong ? trade.EntryPrice - risk.TakeProfitDistance / 2m : trade.EntryPrice + risk.TakeProfitDistance / 2m):F2}.";
        }

        // score < 50 — R:R below 1.5 — do not trade
        decimal tp2to1 = isLong
            ? trade.EntryPrice + risk.StopLossDistance * 2m
            : trade.EntryPrice - risk.StopLossDistance * 2m;
        decimal tp3to1 = isLong
            ? trade.EntryPrice + risk.StopLossDistance * 3m
            : trade.EntryPrice - risk.StopLossDistance * 3m;

        return $"R:R of 1:{rr:F2} does NOT justify the risk. This setup has a negative expected value. " +
               $"Do not take this trade at current levels. " +
               $"Required changes: move TP to at least {tp2to1:F2} for 2:1 R:R, " +
               $"or ideally {tp3to1:F2} for 3:1 R:R. " +
               $"Alternatively, move SL closer to entry to reduce the denominator of the ratio.";
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 7. VOLATILITY / ATR STOP LOSS CALIBRATION  (weight 5%)
    //    Checks if the SL distance is well-calibrated relative to current ATR.
    //    SL too tight → gets stopped out by noise.
    //    SL too wide  → excessive capital at risk.
    //    Ideal: 0.5–2.0× ATR.
    // ──────────────────────────────────────────────────────────────────────────
    private static ScoreComponent CalculateVolatilityScore(Trade trade, IndicatorSnapshot ind, RiskResult risk)
    {
        if (ind.Atr <= 0)
            return new ScoreComponent(ScoreCategory.Volatility, 50m, 0.05m,
                "ATR unavailable — cannot assess SL calibration.",
                "ATR data is missing. Verify indicator engine output before relying on SL placement.");

        decimal slRatio = risk.StopLossDistance / ind.Atr;

        decimal score = slRatio switch
        {
            < 0.5m  => 40m,   // SL too tight — likely to be stopped by noise
            <= 1.5m => 100m,  // Optimal: absorbs noise without excessive risk
            <= 2.5m => 80m,   // Reasonable — slightly wide
            <= 3.5m => 55m,   // Getting wide — higher capital at risk
            _       => 25m    // Too wide — risk is excessive
        };

        string exp = $"Stop Loss distance is {slRatio:F2}× ATR (SL distance: {risk.StopLossDistance:F2}, ATR: {ind.Atr:F2}). " +
                     $"Interpretation: {slRatio switch { < 0.5m => "Too tight — high noise-stop risk", <= 1.5m => "Optimal calibration", <= 2.5m => "Slightly wide but acceptable", <= 3.5m => "Wide — elevated capital risk", _ => "Excessively wide — reduce SL distance" }}. " +
                     $"Ideal range: 0.5–1.5× ATR from entry.";

        string suggestion = BuildVolatilitySuggestion(trade, ind, risk, slRatio, score);

        return new ScoreComponent(ScoreCategory.Volatility, score, 0.05m, exp, suggestion);
    }

    private static string BuildVolatilitySuggestion(Trade trade, IndicatorSnapshot ind, RiskResult risk, decimal slRatio, decimal score)
    {
        bool isLong = trade.Direction == TradeDirection.Long;

        if (score >= 80)
            return $"SL at {trade.StopLoss:F2} is well-calibrated at {slRatio:F2}× ATR ({ind.Atr:F2}). " +
                   $"This distance absorbs typical market noise while keeping risk controlled. No adjustment needed.";

        if (slRatio < 0.5m)
        {
            // SL too tight — suggest widening
            decimal recommendedSl = isLong
                ? trade.EntryPrice - ind.Atr * 1.0m
                : trade.EntryPrice + ind.Atr * 1.0m;
            return $"SL is too tight at {slRatio:F2}× ATR — likely to be hit by normal price noise before the trade has time to develop. " +
                   $"Recommended SL: {recommendedSl:F2} (1.0× ATR = {ind.Atr:F2} from entry). " +
                   $"Compensate by reducing position size proportionally to keep total risk within your % limit.";
        }

        if (slRatio <= 2.5m)
            return $"SL at {trade.StopLoss:F2} is {slRatio:F2}× ATR — slightly wide. " +
                   $"This is acceptable but increases the capital at risk per trade. " +
                   $"If possible, tighten to {(isLong ? trade.EntryPrice - ind.Atr * 1.2m : trade.EntryPrice + ind.Atr * 1.2m):F2} (1.2× ATR) for a better balance.";

        // SL excessively wide
        decimal idealSl = isLong
            ? trade.EntryPrice - ind.Atr * 1.5m
            : trade.EntryPrice + ind.Atr * 1.5m;
        return $"SL at {trade.StopLoss:F2} is {slRatio:F2}× ATR — significantly wider than needed. " +
               $"This exposes more capital per trade than necessary and lowers R:R. " +
               $"Tighten SL to {idealSl:F2} (1.5× ATR from entry) and reduce position size accordingly. " +
               $"Current risk per trade: {risk.RiskAmount:F2} USDT — optimal SL would reduce this by ~{((slRatio - 1.5m) / slRatio * 100m):F0}%.";
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 8. MACRO MARKET CONTEXT  (weight 5%)
    //    Uses EMA200 as a macro trend filter.
    //    Long above EMA200 = bull market context. Short below = bear context.
    //    Also considers how far price is from EMA200 (extreme = reversion risk).
    // ──────────────────────────────────────────────────────────────────────────
    private static ScoreComponent CalculateMarketContextScore(Trade trade, IndicatorSnapshot ind)
    {
        bool isLong   = trade.Direction == TradeDirection.Long;
        bool aligned  = isLong ? ind.CurrentPrice > ind.Ema200 : ind.CurrentPrice < ind.Ema200;

        // Distance from EMA200 in ATR — extreme distance = reversion risk
        decimal distEma200 = ind.Atr > 0 ? Math.Abs(ind.CurrentPrice - ind.Ema200) / ind.Atr : 0m;

        decimal score;
        string exp;

        if (aligned)
        {
            // Aligned with macro trend — but penalize if extremely extended
            score = distEma200 switch
            {
                <= 5.0m  => 90m,
                <= 10.0m => 75m,
                _        => 55m   // Too extended from EMA200 — mean reversion risk
            };
            exp = $"Price ({ind.CurrentPrice:F2}) is {(isLong ? "above" : "below")} EMA200 ({ind.Ema200:F2}) — " +
                  $"macro context is {(isLong ? "bullish" : "bearish")}, aligned with your {(isLong ? "Long" : "Short")}. " +
                  $"Distance from EMA200: {distEma200:F2}× ATR. " +
                  (distEma200 > 5m ? $"Warning: price is extended — mean-reversion risk increases above 5× ATR." : "Extension is within normal range.");
        }
        else
        {
            score = 35m;
            exp = $"Price ({ind.CurrentPrice:F2}) is {(isLong ? "below" : "above")} EMA200 ({ind.Ema200:F2}) — " +
                  $"macro context is {(isLong ? "bearish" : "bullish")}, counter to your {(isLong ? "Long" : "Short")}. " +
                  $"Distance from EMA200: {distEma200:F2}× ATR. Trading against the 200 EMA macro trend elevates risk.";
        }

        string suggestion = BuildMarketContextSuggestion(isLong, aligned, ind, distEma200, score);

        return new ScoreComponent(ScoreCategory.MarketContext, score, 0.05m, exp, suggestion);
    }

    private static string BuildMarketContextSuggestion(
        bool isLong, bool aligned, IndicatorSnapshot ind, decimal distEma200, decimal score)
    {
        if (score >= 75)
            return $"Macro context supports your {(isLong ? "Long" : "Short")}: price is " +
                   $"{(isLong ? "above" : "below")} EMA200 ({ind.Ema200:F2}) by {distEma200:F2}× ATR. " +
                   $"The big-picture trend is on your side — this is a favorable macro environment.";

        if (score >= 55)
        {
            // Aligned but extended
            return $"Macro context is aligned ({(isLong ? "above" : "below")} EMA200 at {ind.Ema200:F2}) but price is " +
                   $"{distEma200:F2}× ATR from EMA200 — extended. " +
                   $"Mean-reversion toward EMA200 is possible. Monitor for sharp corrective moves. " +
                   $"Consider setting a partial profit target closer to EMA200 at {ind.Ema200:F2} as a safety level.";
        }

        // Not aligned with macro trend
        return isLong
            ? $"Price is below EMA200 ({ind.Ema200:F2}) — the macro trend is bearish. " +
              $"Going Long against the 200 EMA trend requires extra caution. " +
              $"Watch BTC dominance and funding rates. Only proceed if this is a confirmed reversal setup " +
              $"with strong catalysts. Consider waiting for price to reclaim EMA200 ({ind.Ema200:F2}) first."
            : $"Price is above EMA200 ({ind.Ema200:F2}) — the macro trend is bullish. " +
              $"Going Short against the 200 EMA trend requires extra caution. " +
              $"Watch BTC dominance and funding rates. Only proceed if this is a confirmed distribution/breakdown setup. " +
              $"Consider waiting for price to break below EMA200 ({ind.Ema200:F2}) first.";
    }
}
