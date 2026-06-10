using RustPlus.ConsoleApp.Utils;
using RustPlusApi.Interfaces;

namespace RustPlus.ConsoleApp.Features;

internal sealed class GetTime(IRustPlus rustPlus)
{
    public async Task GetTimeAsync()
    {
        var response = await rustPlus.GetTimeAsync();
        DisplayUtilities.DisplayJson("Time", response);
    }
}
