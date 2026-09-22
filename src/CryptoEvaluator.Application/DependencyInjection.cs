using CryptoEvaluator.Application.Indicators;
using CryptoEvaluator.Application.Indicators.ATR;
using CryptoEvaluator.Application.Indicators.EMA;
using CryptoEvaluator.Application.Indicators.RSI;
using CryptoEvaluator.Application.Indicators.Volume;
using CryptoEvaluator.Application.Prediction;
using CryptoEvaluator.Application.Risk;
using CryptoEvaluator.Application.Scoring;
using CryptoEvaluator.Application.Warnings;
using Microsoft.Extensions.DependencyInjection;

namespace CryptoEvaluator.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));

        services.AddTransient<IEmaCalculator, EmaCalculator>();
        services.AddTransient<IRsiCalculator, RsiCalculator>();
        services.AddTransient<IAtrCalculator, AtrCalculator>();
        services.AddTransient<IVolumeCalculator, VolumeCalculator>();
        services.AddTransient<IIndicatorEngine, IndicatorEngine>();

        services.AddTransient<IRiskCalculator, RiskCalculator>();
        services.AddTransient<ITradeScoreCalculator, TradeScoreCalculator>();
        services.AddTransient<ITradePredictionEngine, MonteCarloGbmPredictor>();
        services.AddTransient<IHistoricalAnalogPredictor, HistoricalAnalogPredictor>();
        services.AddTransient<IWarningEngine, WarningEngine>();

        return services;
    }
}
