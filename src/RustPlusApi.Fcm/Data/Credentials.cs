namespace RustPlusApi.Fcm.Data;

public sealed record Credentials
{
    public Gcm Gcm { get; init; } = null!;
}

public sealed record Gcm
{
    public ulong AndroidId { get; init; }
    public ulong SecurityToken { get; init; }
}
