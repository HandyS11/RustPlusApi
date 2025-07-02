namespace RustPlusApi.Data.Markers;

public sealed record PlayerMarker : Marker
{
    public string? Name { get; init; }
    public ulong? SteamId { get; init; }
}
