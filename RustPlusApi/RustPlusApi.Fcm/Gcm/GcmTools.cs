using System.Net.Http.Headers;

using CheckinProto;

using ProtoBuf;

namespace RustPlusApi.Fcm.Gcm
{
    public static class GcmTools
    {
        //private const string RegisterUrl = "https://android.clients.google.com/c2dm/register3";
        private const string CheckInUrl = "https://android.clients.google.com/checkin";

        public static async Task<AndroidCheckinResponse> CheckInAsync(long androidId, ulong securityToken)
        {
            var buffer = GetCheckInRequest(androidId, securityToken);

            using var client = new HttpClient();
            using var content = new ByteArrayContent(buffer);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");

            var response = await client.PostAsync(CheckInUrl, content);
            var body = await response.Content.ReadAsByteArrayAsync();

            using var stream = new MemoryStream(body);
            var message = Serializer.Deserialize<AndroidCheckinResponse>(stream);

            return message;
        }

        private static byte[] GetCheckInRequest(long androidId, ulong securityToken)
        {
            var payload = new AndroidCheckinRequest
            {
                UserSerialNumber = 0,
                Checkin = new AndroidCheckinProto
                {
                    Type = DeviceType.DeviceChromeBrowser,
                    ChromeBuild = new ChromeBuildProto
                    {
                        Platform = ChromeBuildProto.Types.Platform.Mac,
                        ChromeVersion = "63.0.3234.0",
                        Channel = ChromeBuildProto.Types.Channel.Stable
                    }
                },
                Version = 3,
                Id = androidId,
                SecurityToken = securityToken
            };
            using var stream = new MemoryStream();
            Serializer.Serialize(stream, payload);
            return stream.ToArray();
        }
    }
}
