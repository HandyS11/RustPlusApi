using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using RustPlusApi.Fcm.Data;

namespace RustPlusApi.Fcm.Converters;

public class BodyConverter : JsonConverter<Body>
{
    public override Body? ReadJson(JsonReader reader, Type objectType, Body? existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        // Check if the value is a string (needs deserialization) or already an object
        if (reader.TokenType == JsonToken.String)
        {
            // Body is provided as a JSON string, deserialize it
            var bodyString = reader.Value!.ToString();
            return JsonConvert.DeserializeObject<Body>(bodyString!);
        }
        else if (reader.TokenType == JsonToken.StartObject)
        {
            // Body is already a JSON object, deserialize directly
            var jObject = JObject.Load(reader);
            return jObject.ToObject<Body>(serializer);
        }

        return null;
    }

    public override void WriteJson(JsonWriter writer, Body? value, JsonSerializer serializer)
    {
        var jsonObject = JObject.FromObject(value!, serializer);
        jsonObject.WriteTo(writer);
    }
}
