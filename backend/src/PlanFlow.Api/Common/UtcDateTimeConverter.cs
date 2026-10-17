using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlanFlow.Api.Common;

/// <summary>
/// Reads every incoming date as UTC. A date without a zone ("2026-10-19T11:00:00") is taken as UTC and one with an
/// offset ("...+02:00") is converted, so comparisons against UtcNow are right and the database never gets a
/// Local/Unspecified value (which it rejects with a 500).
/// </summary>
public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        ToUtc(reader.GetDateTime());

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);

    internal static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}

public sealed class NullableUtcDateTimeConverter : JsonConverter<DateTime?>
{
    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Null ? null : UtcDateTimeConverter.ToUtc(reader.GetDateTime());

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value is { } date) writer.WriteStringValue(date);
        else writer.WriteNullValue();
    }
}
