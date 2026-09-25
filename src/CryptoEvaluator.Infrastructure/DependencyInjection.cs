using CryptoEvaluator.Application.Common.Options;
using CryptoEvaluator.Application.MarketData.Interfaces;
using CryptoEvaluator.Domain.Interfaces;
using CryptoEvaluator.Infrastructure.MarketData.Binance;
using CryptoEvaluator.Infrastructure.Persistence;
using CryptoEvaluator.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Extensions.Http;
using StackExchange.Redis;

namespace CryptoEvaluator.Infrastructure;

public static class DependencyInjection
{
	public static IServiceCollection AddInfrastructure(
		this IServiceCollection services,
		IConfiguration configuration)
	{
		// Database
		var connectionString =
			configuration.GetConnectionString("DefaultConnection");

		if (string.IsNullOrWhiteSpace(connectionString))
		{
			throw new InvalidOperationException(
				"Connection string 'DefaultConnection' is not configured.");
		}

		services.AddDbContext<AppDbContext>(options =>
			options.UseNpgsql(connectionString, npgsqlOptions =>
				npgsqlOptions.EnableRetryOnFailure(
					maxRetryCount: 3,
					maxRetryDelay: TimeSpan.FromSeconds(5),
					errorCodesToAdd: null)));

		// Unit of Work
		services.AddScoped<IUnitOfWork>(
			sp => sp.GetRequiredService<AppDbContext>());

		// Repositories
		services.AddScoped<ITradeRepository, TradeRepository>();
		services.AddScoped<ITradeEvaluationRepository, TradeEvaluationRepository>();
		services.AddScoped<ITradePredictionRepository, TradePredictionRepository>();

		// Market Data Options
		services.Configure<MarketDataOptions>(
			configuration.GetSection(MarketDataOptions.SectionName));
		services.AddSingleton(
			configuration.GetSection(MarketAnalysisOptions.SectionName).Get<MarketAnalysisOptions>()
			?? new MarketAnalysisOptions());

		// Binance Market Data HTTP Client
		services.AddHttpClient<IMarketDataProvider, BinanceMarketDataProvider>(
			(sp, client) =>
			{
				var options =
					sp.GetRequiredService<IOptions<MarketDataOptions>>().Value;

				if (!string.IsNullOrWhiteSpace(options.BaseUrl))
				{
					client.BaseAddress = new Uri(options.BaseUrl);
				}

				client.Timeout =
					TimeSpan.FromSeconds(options.TimeoutSeconds);
			})
			.AddPolicyHandler(
				HttpPolicyExtensions
					.HandleTransientHttpError()
					.WaitAndRetryAsync(
						2,
						retryAttempt =>
							TimeSpan.FromMilliseconds(
								500 * Math.Pow(2, retryAttempt))));

		// Redis / Memory Cache
		var redisConnection =
			configuration.GetConnectionString("Redis");

		if (!string.IsNullOrWhiteSpace(redisConnection))
		{
			var redisUri = new Uri(redisConnection);

			var userInfo = redisUri.UserInfo.Split(':', 2);

			if (string.IsNullOrWhiteSpace(redisUri.Host))
			{
				throw new InvalidOperationException(
					$"Invalid Redis URL: host is empty. URL: {redisConnection}");
			}

			var redisOptions = new ConfigurationOptions();

			redisOptions.EndPoints.Add(
				redisUri.Host,
				redisUri.Port > 0 ? redisUri.Port : 6379);

			redisOptions.Ssl =
				redisUri.Scheme.Equals(
					"rediss",
					StringComparison.OrdinalIgnoreCase);

			redisOptions.AbortOnConnectFail = false;

			if (userInfo.Length == 2)
			{
				redisOptions.User =
					Uri.UnescapeDataString(userInfo[0]);

				redisOptions.Password =
					Uri.UnescapeDataString(userInfo[1]);
			}

			services.AddStackExchangeRedisCache(options =>
			{
				options.ConfigurationOptions = redisOptions;
				options.InstanceName = "CryptoEvaluator:";
			});
		}
		else
		{
			services.AddDistributedMemoryCache();
		}

		// Binance Futures WebSocket ticker background service
		// Maintains a persistent WS stream → writes real-time prices to cache
		// BinanceMarketDataProvider.GetTickerAsync reads from cache first (sub-ms)
		services.AddSingleton<RealtimeTickerStore>();
		services.AddHostedService<BinanceTickerWebSocketService>();

		return services;
	}
}

