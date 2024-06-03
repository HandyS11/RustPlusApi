namespace RustPlusApi.Fcm.Data
{
    public sealed class Credentials
    {
        public Keys Keys { get; set; } = null!;
        public Gcm Gcm { get; set; } = null!;
    }

    public sealed class Keys
    {
        public string PrivateKey { get; set; } = null!;
        public string PublicKey { get; set; } = null!;
        public string AuthSecret { get; set; } = null!;
    }

    public sealed class Gcm
    {
        public ulong AndroidId { get; set; }
        public ulong SecurityToken { get; set; }
    }
}
