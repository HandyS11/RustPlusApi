using System.Text.Json;
using System.Text.Json.Serialization;

namespace RustPlusApi.Fcm.Converters;

public class Int32StringConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var value))
            return value;
        return int.Parse(reader.GetString() ?? throw new JsonException());
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}