using CryptoEvaluator.Domain.Entities;
using CryptoEvaluator.Domain.Enums;
using CryptoEvaluator.Domain.Models;

namespace CryptoEvaluator.Application.Warnings;

public interface IWarningEngine
{
    IReadOnlyList<TradeWarning> GenerateWarnings(
        Trade trade,
        IndicatorSnapshot indicators,
        RiskResult risk,
        DateTime lastCandleTime);
}

public class WarningEngine : IWarningEngine
{
    public IReadOnlyList<TradeWarning> GenerateWarnings(
        Trade trade,
        IndicatorSnapshot indicators,
        RiskResult risk,
        DateTime lastCandleTime)
    {
        var warnings = new List<TradeWarning>();

        // ── Rule 1: SL_TOO_TIGHT ─────────────────────────────────────────────
        // ΔSL < 0.8 × ATR → stop is inside normal market noise, whipsaw risk.
        if (indicators.Atr > 0 && risk.StopLossDistance < 0.8m * indicators.Atr)
        {
            warnings.Add(new TradeWarning(
                Code: "SL_TOO_TIGHT",
                Message: $"Stop loss distance (${risk.StopLossDistance:F4}) is {risk.StopLossDistance / indicators.Atr:P0} of ATR " +
                         $"(${indicators.Atr:F4}). A stop this tight is inside normal market noise and risks being triggered " +
                         "before price moves in your favour.",
                Severity: WarningSeverity.Warning));
        }

        // ── Rule 2: COUNTER_TREND ─────────────────────────────────────────────
        // Full bearish stack (EMA20 < EMA50 < EMA200) on a Long, or vice versa.
        bool fullBearishStack = indicators.Ema20 < indicators.Ema50 && indicators.Ema50 < indicators.Ema200;
        bool fullBullishStack = indicators.Ema20 > indicators.Ema50 && indicators.Ema50 > indicators.Ema200;

        if (trade.Direction == TradeDirection.Long && fullBearishStack)
        {
            warnings.Add(new TradeWarning(
                Code: "COUNTER_TREND",
                Message: $"Long entry while EMA20 ({indicators.Ema20:F2}) < EMA50 ({indicators.Ema50:F2}) < EMA200 " +
                         $"({indicators.Ema200:F2}). Setup is counter to the primary downtrend on this timeframe.",
                Severity: WarningSeverity.Danger));
        }
        else if (trade.Direction == TradeDirection.Short && fullBullishStack)
        {
            warnings.Add(new TradeWarning(
                Code: "COUNTER_TREND",
                Message: $"Short entry while EMA20 ({indicators.Ema20:F2}) > EMA50 ({indicators.Ema50:F2}) > EMA200 " +
                         $"({indicators.Ema200:F2}). Setup is counter to the primary uptrend on this timeframe.",
                Severity: WarningSeverity.Danger));
        }

        // ── Rule 3: HIGH_LEVERAGE ─────────────────────────────────────────────
        // Leverage > 20x OR required margin exceeds 50 % of account balance.
        decimal marginToBalanceRatio = trade.AccountBalance > 0
            ? risk.MarginRequired / trade.AccountBalance
            : 0m;

        if (trade.Leverage > 20m || marginToBalanceRatio > 0.50m)
        {
            string leverageDetail = trade.Leverage > 20m
                ? $"Leverage is {trade.Leverage:F0}×"
                : string.Empty;
            string marginDetail = marginToBalanceRatio > 0.50m
                ? $"Required margin (${risk.MarginRequired:F2}) is {marginToBalanceRatio:P0} of account balance"
                : string.Empty;
            string detail = string.Join("; ", new[] { leverageDetail, marginDetail }
                .Where(s => !string.IsNullOrEmpty(s)));

            warnings.Add(new TradeWarning(
                Code: "HIGH_LEVERAGE",
                Message: $"{detail}. Excessive leverage dramatically increases liquidation risk on sudden price swings.",
                Severity: WarningSeverity.Warning));
        }

        // ── Rule 4: STALE_DATA ────────────────────────────────────────────────
        // DataAge > 2 × candle duration → model calibrated on outdated data.
        TimeSpan candleDuration = TimeframeToCandleDuration(trade.Timeframe);
        TimeSpan dataAge = DateTime.UtcNow - lastCandleTime;
        if (dataAge > candleDuration * 2)
        {
            double staleness = dataAge / candleDuration;
            warnings.Add(new TradeWarning(
                Code: "STALE_DATA",
                Message: $"Market data is {staleness:F1}× the candle interval old " +
                         $"(last candle at {lastCandleTime:HH:mm} UTC). " +
                         "Prediction parameters may not reflect current market conditions.",
                Severity: WarningSeverity.Info));
        }

        // ── Rule 5: POOR_RR ───────────────────────────────────────────────────
        // RR < 1.5 → reward does not justify the risk taken.
        if (risk.RiskRewardRatio < 1.5m)
        {
            warnings.Add(new TradeWarning(
                Code: "POOR_RR",
                Message: $"Risk/reward ratio is 1:{risk.RiskRewardRatio:F2}, below the recommended minimum of 1:1.5. " +
                         "A positive expectancy is difficult to sustain at this ratio.",
                Severity: WarningSeverity.Warning));
        }

        // ── Supplementary: HIGH_ACCOUNT_RISK ─────────────────────────────────
        // User-configured risk percent exceeds the prudent 2 % per-trade limit.
        if (trade.RiskPercent > 2.0m)
        {
            warnings.Add(new TradeWarning(
                Code: "HIGH_ACCOUNT_RISK",
                Message: $"Configured risk per trade ({trade.RiskPercent:F1}%) exceeds the recommended 2% limit. " +
                         "A single losing streak can cause disproportionate drawdown.",
                Severity: WarningSeverity.Warning));
        }

        // ── Supplementary: LOW_VOLUME ─────────────────────────────────────────
        // Current candle volume significantly below 20-period average.
        if (indicators.VolumeRatio < 0.5m)
        {
            warnings.Add(new TradeWarning(
                Code: "LOW_VOLUME",
                Message: $"Volume ratio is {indicators.VolumeRatio:F2}× the 20-period average. " +
                         "Low liquidity increases slippage risk and may indicate a false breakout.",
                Severity: WarningSeverity.Warning));
        }

        // ── Supplementary: RSI_OVERBOUGHT / RSI_OVERSOLD ─────────────────────
        if (trade.Direction == TradeDirection.Long && indicators.Rsi > 80m)
        {
            warnings.Add(new TradeWarning(
                Code: "RSI_OVERBOUGHT",
                Message: $"RSI is {indicators.Rsi:F1} — momentum is overheated for a Long entry. " +
                         "Price is susceptible to a mean-reversion pullback.",
                Severity: WarningSeverity.Warning));
        }
        else if (trade.Direction == TradeDirection.Short && indicators.Rsi < 20m)
        {
            warnings.Add(new TradeWarning(
                Code: "RSI_OVERSOLD",
                Message: $"RSI is {indicators.Rsi:F1} — price is deeply oversold for a Short entry. " +
                         "A bounce/relief rally is plausible before further downside.",
                Severity: WarningSeverity.Warning));
        }

        // ── [§8 Extension] SETUP_DEGRADING (Bayesian score degradation) ──────
        // Triggered when current active trade score has degraded by >20 points from initial setup score
        if (trade.LatestEvaluation != null && trade.LatestEvaluation.Score > 0)
        {
            decimal initialScore = trade.LatestEvaluation.Score;
            // Evaluate instantaneous degraded score indicator (e.g. if counter-trend or momentum collapsed)
            bool momentumCollapsed = (trade.Direction == TradeDirection.Long && indicators.Rsi < 35m) ||
                                    (trade.Direction == TradeDirection.Short && indicators.Rsi > 65m);
            if (momentumCollapsed && initialScore >= 70m)
            {
                warnings.Add(new TradeWarning(
                    Code: "SETUP_DEGRADING",
                    Message: $"Active setup score has degraded significantly (>20pt drop from initial {initialScore:F0}). " +
                             "Indicator alignment has collapsed while trade is in progress.",
                    Severity: WarningSeverity.Warning));
            }
        }

        return warnings;
    }

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
