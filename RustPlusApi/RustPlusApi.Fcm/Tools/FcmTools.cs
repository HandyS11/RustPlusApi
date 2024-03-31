using System.Security.Cryptography;
using System.Text.Json;

using RustPlusApi.Fcm.Data;

namespace RustPlusApi.Fcm.Tools
{
    public static class FcmTools
    {
        private const string FcmSubscribeUrl = "https://fcm.googleapis.com/fcm/connect/subscribe";
        private const string FcmEndpoint = "https://fcm.googleapis.com/fcm/send";

        private static readonly HttpClient HttpClient = new();

        public static async Task<Tuple<Keys, FcmCredentials>> RegisterFcmAsync(string senderId, string token)
        {
            var keys = CreateKeys();
            var response = await HttpClient.PostAsync(FcmSubscribeUrl, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "authorized_entity", senderId },
                { "endpoint", $"{FcmEndpoint}/{token}" },
                { "encryption_key", keys.PublicKey },
                { "encryption_auth", keys.AuthSecret },
            }));

            var responseText = await response.Content.ReadAsStringAsync();
            var fcmResponse = JsonSerializer.Deserialize<FcmCredentials>(responseText);

            return new Tuple<Keys, FcmCredentials>(keys, fcmResponse!);
        }

        private static Keys CreateKeys()
        {
            using var dh = ECDiffieHellman.Create();
            var privateKey = Convert.ToBase64String(dh.ExportECPrivateKey());
            var publicKey = Convert.ToBase64String(dh.PublicKey.ExportSubjectPublicKeyInfo());

            var authSecretBytes = new byte[16];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(authSecretBytes);
            var authSecret = Convert.ToBase64String(authSecretBytes);

            return new Keys
            {
                PrivateKey = privateKey,
                PublicKey = publicKey,
                AuthSecret = authSecret
            };
        }
    }
}
