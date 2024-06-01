using RustPlusApi.Data.Notes;

namespace RustPlusApi.Data
{
    public class TeamInfo
    {
        public ulong LeaderSteamId { get; set; }
        public IEnumerable<MemberInfo>? Members { get; set; }
        public DeathNote? DeathNote { get; set; }
        public IEnumerable<PlayerNote>? Notes { get; set; }
        public IEnumerable<PlayerNote>? LeaderNotes { get; set; }
    }
}
