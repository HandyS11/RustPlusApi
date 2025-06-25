namespace RustPlusApi.Fcm.Data;

public sealed record Credentials
{
    public Gcm Gcm { get; set; } = null!;
}

public sealed record Gcm
{
    public ulong AndroidId { get; set; }
    public ulong SecurityToken { get; set; }
}
