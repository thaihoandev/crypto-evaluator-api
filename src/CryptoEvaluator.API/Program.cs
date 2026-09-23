using System.Text.Json.Serialization;
using CryptoEvaluator.API.ModelBinding;
using CryptoEvaluator.API.Middleware;
using CryptoEvaluator.API.Serialization;
using CryptoEvaluator.Application;
using CryptoEvaluator.Infrastructure;
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

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:3000", "https://localhost:3000", "http://localhost:5173")
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
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program { }
