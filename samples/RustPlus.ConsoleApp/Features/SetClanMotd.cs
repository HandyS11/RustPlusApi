using RustPlus.ConsoleApp.Utils;
using RustPlusApi.Interfaces;

namespace RustPlus.ConsoleApp.Features;

internal sealed class SetClanMotd(IRustPlus rustPlus)
{
    public async Task SetClanMotdAsync(string message)
    {
        var response = await rustPlus.SetClanMotdAsync(message);
        DisplayUtilities.DisplayJson("SetClanMotd", response);
    }
}
