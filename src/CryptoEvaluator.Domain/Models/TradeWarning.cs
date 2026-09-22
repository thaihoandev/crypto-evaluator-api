using CryptoEvaluator.Domain.Enums;

namespace CryptoEvaluator.Domain.Models;

public record TradeWarning(
    string Code,
    string Message,
    WarningSeverity Severity);
