namespace RustPlusApi.Data;

public sealed record ServerMapMonument
{
    public string? Name { get; init; }
    public float? X { get; init; }
    public float? Y { get; init; }
}
