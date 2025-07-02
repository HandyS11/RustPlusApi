using System.Drawing;

namespace RustPlusApi.Data;

public sealed record ServerMap
{
    public uint? Height { get; init; }
    public uint? Width { get; init; }
    public int? OceanMargin { get; init; }
    public Color Background { get; init; }
    public List<ServerMapMonument>? Monuments { get; init; }
    public byte[]? JpgImage { get; init; }
}
