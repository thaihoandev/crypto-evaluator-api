namespace CryptoEvaluator.Application.Common.Options;

/// <summary>Configurable thresholds for pre-trade market analysis.</summary>
public class MarketAnalysisOptions
{
    public const string SectionName = "MarketAnalysis";

    public int CandleRequestLimit { get; set; } = 300;
    public int MinimumClosedCandles { get; set; } = 250;
    public decimal StopLossAtrMultiplier { get; set; } = 1.5m;
    public decimal TakeProfitRiskRewardRatio { get; set; } = 2m;
    public decimal MinimumSetupScore { get; set; } = 65m;
    public decimal MinimumWinProbability { get; set; } = 55m;
    public decimal MinimumExpectedRMultiple { get; set; } = 0m;
    public decimal MaximumNoHitProbability { get; set; } = 35m;
    public int SimulatedPaths { get; set; } = 5000;
    public decimal PreviewAccountBalance { get; set; } = 10000m;
    public decimal PreviewRiskPercent { get; set; } = 1m;
    public decimal PreviewLeverage { get; set; } = 1m;
    public int StructureLookbackCandles { get; set; } = 120;
    public int PivotStrength { get; set; } = 3;
    public decimal StructureStopBufferAtrMultiplier { get; set; } = 0.25m;
    public decimal EntryZoneAtrMultiplier { get; set; } = 0.25m;
    public decimal MinimumStructureRiskRewardRatio { get; set; } = 1.5m;
}
