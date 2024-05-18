using Newtonsoft.Json;

namespace RustPlusApi.Fcm.Data
{
    public sealed class Credentials
    {
        [JsonProperty(PropertyName = "keys")]
        public Keys Keys { get; set; } = null!;
        [JsonProperty(PropertyName = "fcm")]
        public FcmCredentials Fcm { get; set; } = null!;
        [JsonProperty(PropertyName = "gcm")]
        public GcmCredentials Gcm { get; set; } = null!;
    }

    public sealed class Keys
    {
        [JsonProperty(PropertyName = "privateKey")]
        public string PrivateKey { get; set; } = null!;

        [JsonProperty(PropertyName = "publicKey")]
        public string PublicKey { get; set; } = null!;

        [JsonProperty(PropertyName = "authSecret")]
        public string AuthSecret { get; set; } = null!;
    }

    public sealed class FcmCredentials
    {
        [JsonProperty(PropertyName = "token")]
        public string Token { get; set; } = null!;
        [JsonProperty(PropertyName = "pushSet")]
        public string PushSet { get; set; } = null!;
    }

    public sealed class GcmCredentials
    {
        [JsonProperty(PropertyName = "token")]
        public string Token { get; set; } = null!;
        [JsonProperty(PropertyName = "androidId")]
        public ulong AndroidId { get; set; }
        [JsonProperty(PropertyName = "securityToken")]
        public ulong SecurityToken { get; set; }
        [JsonProperty(PropertyName = "appId")]
        public string AppId { get; set; } = null!;
    }
}
