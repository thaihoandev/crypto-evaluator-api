using CryptoEvaluator.Domain.Enums;

namespace CryptoEvaluator.Domain.Models;

/// <summary>
/// Represents one scored dimension of a trade evaluation.
/// <para><b>SuggestedAction</b> is produced by the BE scoring engine using real indicator
/// values (RSI, ATR, EMA distances, R:R ratio, etc.) so the frontend only needs to render
/// it — no client-side inference required.</para>
/// </summary>
public record ScoreComponent(
    ScoreCategory Category,
    decimal Score,
    decimal Weight,
    string Explanation,
    string SuggestedAction);
