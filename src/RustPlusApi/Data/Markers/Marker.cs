namespace RustPlusApi.Data.Markers;

public record Marker
{
    public uint? Id { get; init; }
    public float? X { get; init; }
    public float? Y { get; init; }
}
