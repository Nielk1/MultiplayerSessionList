using System;
using System.Text.Json;
using System.Text.Json.Serialization;

public class FaultTolerantIntConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        try
        {
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int value))
                return value;
            if (reader.TokenType == JsonTokenType.String && int.TryParse(reader.GetString(), out value))
                return value;
        }
        catch { }
        return default; // fallback value (0)
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value);
}

// Add this for string <-> int coercion
public class FaultTolerantStringConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        try
        {
            if (reader.TokenType == JsonTokenType.String)
                return reader.GetString();
            if (reader.TokenType == JsonTokenType.Number)
                return reader.GetInt64().ToString();
        }
        catch { }
        return string.Empty; // fallback value
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);
}