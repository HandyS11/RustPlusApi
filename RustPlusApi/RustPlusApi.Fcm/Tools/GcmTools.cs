﻿using System.Diagnostics;

using CheckinProto;

using Google.Protobuf;

using ProtoBuf;

using RustPlusApi.Fcm.Data;

namespace RustPlusApi.Fcm.Tools
{
    public static class GcmTools
    {
        private static readonly HttpClient HttpClient = new();

        private const string CheckInUrl = "https://android.clients.google.com/checkin";
        private const string RegisterUrl = "https://android.clients.google.com/c2dm/register3";

        private static readonly string ServerKey = Convert.ToBase64String(Constants.ServerKey);

        public static async Task<GcmCredentials> RegisterAsync(string appId)
        {
            var options = await CheckInAsync();
            var credentials = await DoRegisterAsync(options, appId);
            return credentials;
        }

        public static async Task<AndroidCheckinResponse> CheckInAsync(ulong? androidId = null, ulong? securityToken = null)
        {
            var id = (androidId != null) ? (long)androidId : (long?)null;
            var requestBody = GetCheckInRequest(id, securityToken);

            var request = new HttpRequestMessage(HttpMethod.Post, CheckInUrl)
            {
                Headers =
                {
                    { "Content-Type", "application/x-protobuf" },
                },
                Content = new ByteArrayContent(requestBody.ToByteArray())
            };
            var response = await HttpClient.SendAsync(request);
            var data = await response.Content.ReadAsByteArrayAsync();

            using var stream = new MemoryStream(data);
            var message = Serializer.Deserialize<AndroidCheckinResponse>(stream);

            return message;
        }

        private static AndroidCheckinRequest GetCheckInRequest(long? androidId = null, ulong? securityToken = null)
        {
            return new AndroidCheckinRequest
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
                Id = androidId ?? default,
                SecurityToken = securityToken ?? default
            };
        }

        private static async Task<GcmCredentials> DoRegisterAsync(AndroidCheckinResponse option, string appId)
        {
            var body = new Dictionary<string, string>
            {
                { "app", "org.chromium.linux" },
                { "X-subtype", appId },
                { "device", option.AndroidId.ToString() },
                { "sender", ServerKey }
            };

            var response = await PostRegisterAsync(option.AndroidId, option.SecurityToken, body);
            var token = response.Split('=')[1];

            return new GcmCredentials
            {
                Token = token,
                AndroidId = option.AndroidId,
                SecurityToken = option.SecurityToken,
                AppId = appId
            };
        }

        private static async Task<string> PostRegisterAsync(ulong androidId, ulong securityToken, Dictionary<string, string> body, int retry = 0)
        {
            while (true)
            {
                var request = new HttpRequestMessage(HttpMethod.Post, RegisterUrl)
                {
                    Headers =
                    {
                        { "Authorization", $"AidLogin {androidId}:{securityToken}" },
                        { "Content-Type", "application/x-www-form-urlencoded" }
                    },
                    Content = new FormUrlEncodedContent(body)
                };

                var response = await HttpClient.SendAsync(request);
                var responseText = await response.Content.ReadAsStringAsync();

                if (!responseText.Contains("Error")) return responseText;
                Debug.WriteLine($"Register request has failed with {responseText}");
                if (retry >= 5)
                {
                    throw new Exception("GCM register has failed");
                }

                Debug.WriteLine($"Retry... {retry + 1}");
                await Task.Delay(1000);
                retry++;
            }
        }
    }
}
