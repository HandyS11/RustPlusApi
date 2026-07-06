namespace RustPlusApi.Data;

/// <summary>Color carried by a map marker, mapped from the server's <c>Vector4</c> (RGBA components, 0–1).</summary>
public sealed record MarkerColor
{
    /// <summary>Red component (0–1), or <see langword="null"/> when the server omitted it.</summary>
    public float? R { get; init; }

    /// <summary>Green component (0–1), or <see langword="null"/> when the server omitted it.</summary>
    public float? G { get; init; }

    /// <summary>Blue component (0–1), or <see langword="null"/> when the server omitted it.</summary>
    public float? B { get; init; }

    /// <summary>Alpha component (0–1), or <see langword="null"/> when the server omitted it.</summary>
    public float? A { get; init; }
}
