using RustPlus.ConsoleApp.Utils;
using RustPlusApi.Interfaces;

namespace RustPlus.ConsoleApp.Features;

internal sealed class PromoteToLeader(IRustPlus rustPlus)
{
    public async Task PromoteToLeaderAsync(ulong steamId)
    {
        var response = await rustPlus.PromoteToLeaderAsync(steamId);
        DisplayUtilities.DisplayJson("PromoteToLeader", response);
    }
}