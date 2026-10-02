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

    /// <summary>
    /// Whether <see cref="Name"/> is a custom name rather than a built-in monument token, or
    /// <see langword="null"/> if the server is too old to report it.
    /// </summary>
    public bool? IsCustomName { get; init; }
}
