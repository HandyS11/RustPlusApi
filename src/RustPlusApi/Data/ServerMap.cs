using System.Drawing;

namespace RustPlusApi.Data;

/// <summary>Server map image and monument list returned by <c>GetMapAsync</c>.</summary>
public sealed record ServerMap
{
    /// <summary>Height of the map in game units.</summary>
    public uint? Height { get; init; }

    /// <summary>Width of the map in game units.</summary>
    public uint? Width { get; init; }

    /// <summary>Width of the ocean margin around the playable area, in game units.</summary>
    public int? OceanMargin { get; init; }

    /// <summary>Background colour of the map (ocean colour).</summary>
    public Color Background { get; init; }

    /// <summary>List of monuments present on the map.</summary>
    public List<ServerMapMonument>? Monuments { get; init; }

    /// <summary>Raw JPEG image bytes of the map tile, if available.</summary>
    public byte[]? JpgImage { get; init; }
}
