using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CryptoEvaluator.API.Controllers;
using CryptoEvaluator.Application.Evaluations.Models;
using CryptoEvaluator.Application.Trades.Models;
using CryptoEvaluator.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace CryptoEvaluator.IntegrationTests;

public class TradesControllerIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public TradesControllerIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreateAndEvaluateTrade_EndToEndFlow_ShouldReturn200AndValidEvaluation()
    {
        // 0. Fetch current ticker price so SL/TP are relative to current market price
        decimal currentPrice = 100000m;
        var tickerResponse = await _client.GetAsync("/api/v1/trades/ticker/BTCUSDT");
        if (tickerResponse.IsSuccessStatusCode)
        {
            using var doc = await JsonDocument.ParseAsync(await tickerResponse.Content.ReadAsStreamAsync());
            if (doc.RootElement.TryGetProperty("price", out var priceElement) && priceElement.GetDecimal() > 0)
            {
                currentPrice = priceElement.GetDecimal();
            }
        }

        decimal entryPrice = Math.Round(currentPrice, 2);
        decimal stopLoss = Math.Round(currentPrice * 0.98m, 2);
        decimal takeProfit = Math.Round(currentPrice * 1.04m, 2);

        // 1. Create Trade
        var createRequest = new CreateTradeRequest(
            UserId: Guid.NewGuid(),
            Symbol: "BTCUSDT",
            Direction: TradeDirection.Long,
            Timeframe: Timeframe.H1,
            EntryPrice: entryPrice,
            StopLoss: stopLoss,
            TakeProfit: takeProfit,
            AccountBalance: 10000m,
            RiskPercent: 1.0m,
            Leverage: 5m);

        var createResponse = await _client.PostAsJsonAsync("/api/v1/trades", createRequest, JsonOptions);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var trade = await createResponse.Content.ReadFromJsonAsync<TradeResponse>(JsonOptions);
        trade.Should().NotBeNull();
        trade!.Symbol.Should().Be("BTCUSDT");


        // 2. Evaluate Trade (fetches candles from market provider, calculates indicators, score, Monte Carlo prediction)
        var evalRequest = new EvaluateTradeRequest(RandomSeed: 42);
        var evalResponse = await _client.PostAsJsonAsync($"/api/v1/trades/{trade.Id}/evaluate", evalRequest, JsonOptions);
        evalResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var evaluation = await evalResponse.Content.ReadFromJsonAsync<EvaluateTradeResponse>(JsonOptions);
        evaluation.Should().NotBeNull();
        evaluation!.TradeId.Should().Be(trade.Id);
        evaluation.Score.Should().BeGreaterThan(0m);
        evaluation.RiskRewardRatio.Should().Be(2.0m);
        evaluation.RiskAmount.Should().Be(100m);
        evaluation.Prediction.Should().NotBeNull();
        evaluation.Prediction.WinProbability.Should().BeGreaterThan(0m);
        evaluation.Components.Should().NotBeEmpty();

        // 3. Get Prediction Endpoint
        var predResponse = await _client.GetAsync($"/api/v1/trades/{trade.Id}/prediction");
        predResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
