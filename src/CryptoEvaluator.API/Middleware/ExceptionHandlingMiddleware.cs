using System.Net;
using System.Text.Json;

namespace CryptoEvaluator.API.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unhandled exception occurred during request execution.");
            await HandleExceptionAsync(context, ex);
        }
    }

    private static Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var (statusCode, code, message) = exception switch
        {
            ArgumentException argEx => (HttpStatusCode.BadRequest, "INVALID_ARGUMENT", argEx.Message),
            InvalidOperationException invEx => (HttpStatusCode.BadRequest, "INVALID_OPERATION", invEx.Message),
            KeyNotFoundException keyEx => (HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND", keyEx.Message),
            _ => (HttpStatusCode.InternalServerError, "INTERNAL_SERVER_ERROR", "An unexpected error occurred while processing your request.")
        };

        context.Response.StatusCode = (int)statusCode;

        var responsePayload = new
        {
            code = code,
            message = message
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(responsePayload));
    }
}
