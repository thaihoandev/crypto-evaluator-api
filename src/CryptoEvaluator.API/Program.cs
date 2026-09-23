using System.Text.Json.Serialization;
using CryptoEvaluator.API.ModelBinding;
using CryptoEvaluator.API.Middleware;
using CryptoEvaluator.API.Serialization;
using CryptoEvaluator.Application;
using CryptoEvaluator.Infrastructure;
using CryptoEvaluator.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.Converters.Add(new UtcDateTimeJsonConverter());
    });

builder.Services.Configure<Microsoft.AspNetCore.Mvc.MvcOptions>(options =>
    options.ModelBinderProviders.Insert(0, new UtcDateTimeModelBinderProvider()));

// CORS: merge defaults with any extra origins from config (e.g. Render env var)
var defaultOrigins = new[] { "http://localhost:3000", "https://localhost:3000", "http://localhost:5173" };
var extraOrigins = builder.Configuration["Cors:AllowedOrigins"]
    ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    ?? [];
var allOrigins = defaultOrigins.Union(extraOrigins).ToArray();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(allOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Native .NET 10 OpenAPI (replaces Swashbuckle)
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, _) =>
    {
        document.Info = new()
        {
            Title = "Crypto Trade Evaluator API",
            Version = "v1",
            Description = "API for evaluating cryptocurrency trades using technical indicators, " +
                          "risk parameters, weighted scoring, and Monte Carlo GBM predictions."
        };
        return Task.CompletedTask;
    });
});

// Clean Architecture registrations
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// Auto-apply EF Core migrations on startup (safe for Render deploy)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.UseMiddleware<ExceptionHandlingMiddleware>();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    // Serves OpenAPI spec at /openapi/v1.json
    app.MapOpenApi();

    // Scalar interactive UI at /scalar/v1
    app.MapScalarApiReference(options =>
    {
        options.Title = "Crypto Trade Evaluator API";
        options.Theme = ScalarTheme.DeepSpace;
    });
}

app.UseCors("AllowFrontend");
// NOTE: HttpsRedirection removed — Render terminates TLS at the load balancer;
// the container itself receives plain HTTP on the PORT env var.
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program { }
