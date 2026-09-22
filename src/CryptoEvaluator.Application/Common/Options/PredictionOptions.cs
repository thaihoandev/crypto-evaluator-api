namespace CryptoEvaluator.Application.Common.Options;

public class PredictionOptions
{
    public const string SectionName = "Prediction";

    public int DefaultSimulatedPaths { get; set; } = 10000;
}
