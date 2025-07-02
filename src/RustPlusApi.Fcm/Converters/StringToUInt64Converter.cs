using System.Text.Json;
using System.Text.Json.Serialization;

namespace RustPlusApi.Fcm.Converters;

public class StringToUInt64Converter : JsonConverter<ulong>
{
    public override ulong Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String && ulong.TryParse(reader.GetString(), out var value))
            return value;
        if (reader.TokenType == JsonTokenType.Number)
            return reader.GetUInt64();
        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, ulong value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}