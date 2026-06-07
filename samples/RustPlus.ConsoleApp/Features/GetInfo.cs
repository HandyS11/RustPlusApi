using RustPlus.ConsoleApp.Utils;
using RustPlusApi.Interfaces;

namespace RustPlus.ConsoleApp.Features;

internal sealed class GetInfo(IRustPlus rustPlus)
{
    public async Task GetInfoAsync()
    {
        var response = await rustPlus.GetInfoAsync();
        DisplayUtilities.DisplayJson("Info", response);
    }
}