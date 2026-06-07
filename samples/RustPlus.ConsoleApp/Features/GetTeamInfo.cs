using RustPlus.ConsoleApp.Utils;
using RustPlusApi.Interfaces;

namespace RustPlus.ConsoleApp.Features;

internal sealed class GetTeamInfo(IRustPlus rustPlus)
{
    public async Task GetTeamInfoAsync()
    {
        var response = await rustPlus.GetTeamInfoAsync();
        DisplayUtilities.DisplayJson("TeamInfo", response);
    }
}