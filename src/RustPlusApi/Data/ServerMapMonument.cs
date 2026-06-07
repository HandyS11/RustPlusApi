namespace RustPlusApi.Data;

/// <summary>A named landmark / monument on the server map.</summary>
public sealed record ServerMapMonument
{
    /// <summary>Display name of the monument (e.g. <c>Launch Site</c>).</summary>
    public string? Name { get; init; }

    /// <summary>Horizontal map coordinate (west → east).</summary>
    public float? X { get; init; }

    /// <summary>Vertical map coordinate (south → north).</summary>
    public float? Y { get; init; }
}
