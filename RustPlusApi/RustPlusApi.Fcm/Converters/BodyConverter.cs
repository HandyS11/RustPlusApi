using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using RustPlusApi.Fcm.Data;

namespace RustPlusApi.Fcm.Converters
{
    public class BodyConverter : JsonConverter<Body>
    {
        public override Body? ReadJson(JsonReader reader, Type objectType, Body? existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var bodyString = reader.Value!.ToString();
            return JsonConvert.DeserializeObject<Body>(bodyString!);
        }

        public override void WriteJson(JsonWriter writer, Body? value, JsonSerializer serializer)
        {
            JObject jsonObject = JObject.FromObject(value!, serializer);
            jsonObject.WriteTo(writer);
        }
    }
}
