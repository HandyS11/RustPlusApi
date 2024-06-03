using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RustPlusApi.Fcm.Extensions
{
    public static class StringToFcmMessageString
    {
        public static string ToFcmMessageString(this string message)
        {
            try
            {
                var jsonObject = JObject.Parse(message);

                var bodyString = jsonObject["data"]!["body"]!.ToString();
                var bodyObject = JObject.Parse(bodyString);

                var formattedBody = bodyObject.ToString(Formatting.Indented);
                jsonObject["data"]!["body"] = JToken.Parse(formattedBody);

                return jsonObject.ToString(Formatting.Indented);
            }
            catch
            {
                return message;
            }
        }
    }
}
