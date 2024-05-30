namespace RustPlusApi.Fcm.Data
{
    public sealed class Credentials
    {
        public Keys Keys { get; set; } = null!;
        public FcmCredentials Fcm { get; set; } = null!;
        public GcmCredentials Gcm { get; set; } = null!;
    }

    public sealed class Keys
    {
        public string PrivateKey { get; set; } = null!;
        public string PublicKey { get; set; } = null!;
        public string AuthSecret { get; set; } = null!;
    }

    public sealed class FcmCredentials
    {
        public string Token { get; set; } = null!;
        public string PushSet { get; set; } = null!;
    }

    public sealed class GcmCredentials
    {
        public string Token { get; set; } = null!;
        public ulong AndroidId { get; set; }
        public ulong SecurityToken { get; set; }
        public string AppId { get; set; } = null!;
    }
}
