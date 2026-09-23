using System.Globalization;
using CryptoEvaluator.API.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace CryptoEvaluator.API.ModelBinding;

/// <summary>Binds query, route, and form DateTime values as UTC.</summary>
public sealed class UtcDateTimeModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ValueProviderResult result = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (result == ValueProviderResult.None)
            return Task.CompletedTask;

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, result);
        string? rawValue = result.FirstValue;
        if (string.IsNullOrWhiteSpace(rawValue))
            return Task.CompletedTask;

        if (DateTimeOffset.TryParse(
                rawValue,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
                out DateTimeOffset timestamp))
        {
            bindingContext.Result = ModelBindingResult.Success(timestamp.UtcDateTime);
        }
        else
        {
            bindingContext.ModelState.TryAddModelError(
                bindingContext.ModelName,
                "Timestamp must be a valid ISO-8601 value.");
        }

        return Task.CompletedTask;
    }
}

public sealed class UtcDateTimeModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context) =>
        Nullable.GetUnderlyingType(context.Metadata.ModelType) == typeof(DateTime) ||
        context.Metadata.ModelType == typeof(DateTime)
            ? new UtcDateTimeModelBinder()
            : null;
}
