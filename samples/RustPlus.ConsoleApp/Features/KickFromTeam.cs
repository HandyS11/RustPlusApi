using RustPlus.ConsoleApp.Utils;
using RustPlusApi.Interfaces;

namespace RustPlus.ConsoleApp.Features;

internal sealed class KickFromTeam(IRustPlus rustPlus)
{
    public async Task KickFromTeamAsync(ulong steamId)
    {
        var response = await rustPlus.KickFromTeamAsync(steamId);
        DisplayUtilities.DisplayJson("KickFromTeam", response);
    }
}
