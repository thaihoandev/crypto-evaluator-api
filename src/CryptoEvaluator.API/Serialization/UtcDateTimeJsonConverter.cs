using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CryptoEvaluator.API.Serialization;

/// <summary>Serializes all API timestamps as UTC and accepts offset-less input as UTC.</summary>
public sealed class UtcDateTimeJsonConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("A timestamp must be an ISO-8601 string.");

        string value = reader.GetString() ?? throw new JsonException("A timestamp is required.");
        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
                out DateTimeOffset timestamp))
        {
            throw new JsonException("A timestamp must be a valid ISO-8601 value.");
        }

        return timestamp.UtcDateTime;
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(ToUtc(value).ToString("O", CultureInfo.InvariantCulture));

    internal static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
}
