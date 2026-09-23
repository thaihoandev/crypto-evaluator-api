namespace CryptoEvaluator.Domain.Enums;

public enum MarketAnalysisBlocker
{
    InsufficientClosedCandles = 1,
    StaleMarketData = 2,
    LowConfidence = 3,
    LowSetupScore = 4,
    LowWinProbability = 5,
    LowExpectedRMultiple = 6,
    HighNoHitProbability = 7,
    OutsideEntryZone = 8,
    PoorMarketStructureRiskReward = 9
}
