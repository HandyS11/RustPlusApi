using System.Drawing;

namespace RustPlusApi.Data;

public class TeamMessage
{
    public ulong SteamId { get; set; }
    public string Name { get; set; } = null!;
    public string Message { get; set; } = null!;
    public Color Color { get; set; }
    public DateTime Time { get; set; }
}
