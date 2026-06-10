using RustPlus.ConsoleApp.Utils;
using RustPlusApi.Interfaces;

namespace RustPlus.ConsoleApp.Features;

internal sealed class GetClanInfo(IRustPlus rustPlus)
{
    public async Task GetClanInfoAsync()
    {
        var response = await rustPlus.GetClanInfoAsync();
        DisplayUtilities.DisplayJson("ClanInfo", response);
    }
}
