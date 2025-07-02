using System.Drawing;

namespace RustPlusApi.Data;

public record TeamMessage
{
    public ulong SteamId { get; init; }
    public string Name { get; init; } = null!;
    public string Message { get; init; } = null!;
    public Color Color { get; init; }
    public DateTime Time { get; init; }
}
