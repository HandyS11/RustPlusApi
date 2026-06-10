using RustPlus.ConsoleApp.Utils;
using RustPlusApi.Interfaces;

namespace RustPlus.ConsoleApp.Features;

internal sealed class SendClanMessage(IRustPlus rustPlus)
{
    public async Task SendClanMessageAsync(string message)
    {
        var response = await rustPlus.SendClanMessageAsync(message);
        DisplayUtilities.DisplayJson("SendClanMessage", response);
    }
}
