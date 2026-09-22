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
			options.UseNpgsql(connectionString));

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
			services.AddStackExchangeRedisCache(options =>
			{
				options.Configuration = redisConnection;
				options.InstanceName = "CryptoEvaluator:";
			});
		}
		else
		{
			services.AddDistributedMemoryCache();
		}

		return services;
	}
}