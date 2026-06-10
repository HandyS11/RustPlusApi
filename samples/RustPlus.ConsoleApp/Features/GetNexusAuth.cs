using RustPlus.ConsoleApp.Utils;
using RustPlusApi.Interfaces;

namespace RustPlus.ConsoleApp.Features;

internal sealed class GetNexusAuth(IRustPlus rustPlus)
{
    public async Task GetNexusAuthAsync(string appKey)
    {
        var response = await rustPlus.GetNexusAuthAsync(appKey);
        DisplayUtilities.DisplayJson("NexusAuth", response);
    }
}
