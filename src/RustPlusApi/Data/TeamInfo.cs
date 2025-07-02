using RustPlusApi.Data.Notes;

namespace RustPlusApi.Data;

public sealed record TeamInfo
{
    public ulong LeaderSteamId { get; init; }
    public IEnumerable<MemberInfo>? Members { get; init; }
    public DeathNote? DeathNote { get; init; }
    public IEnumerable<PlayerNote>? Notes { get; init; }
    public IEnumerable<PlayerNote>? LeaderNotes { get; init; }
}
