using System.Drawing;

namespace RustPlusApi.Data;

/// <summary>Server map image and monument list returned by <c>GetMapAsync</c>.</summary>
/// <remarks>
/// <para><see cref="Width"/>, <see cref="Height"/> and <see cref="OceanMargin"/> are pixel
/// measurements of <see cref="JpgImage"/> — only marker/monument coordinates and
/// <c>ServerInfo.MapSize</c> are world units. The canonical world→pixel transform (world origin
/// bottom-left, image origin top-left — hence the Y flip):</para>
/// <code>
/// px = worldX * ((Width  - 2 * OceanMargin) / ServerInfo.MapSize) + OceanMargin
/// py = Height - (worldY * ((Height - 2 * OceanMargin) / ServerInfo.MapSize) + OceanMargin)
/// </code>
/// </remarks>
public sealed record ServerMap
{
    /// <summary>Height of <see cref="JpgImage"/> in pixels.</summary>
    public uint? Height { get; init; }

    /// <summary>Width of <see cref="JpgImage"/> in pixels.</summary>
    public uint? Width { get; init; }

    /// <summary>Width of the ocean border baked into <see cref="JpgImage"/>, in pixels.</summary>
    public int? OceanMargin { get; init; }

    /// <summary>Background colour of the map (ocean colour).</summary>
    public Color Background { get; init; }

    /// <summary>List of monuments present on the map.</summary>
    public List<ServerMapMonument>? Monuments { get; init; }

    /// <summary>Raw JPEG image bytes of the map tile, if available.</summary>
    public byte[]? JpgImage { get; init; }
}
