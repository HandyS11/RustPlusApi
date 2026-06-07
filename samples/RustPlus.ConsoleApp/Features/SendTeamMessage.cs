using RustPlus.ConsoleApp.Utils;
using RustPlusApi.Interfaces;

namespace RustPlus.ConsoleApp.Features;

internal sealed class SendTeamMessage(IRustPlus rustPlus)
{
    public async Task SendTeamMessageAsync(string message)
    {
        var response = await rustPlus.SendTeamMessageAsync(message);
        DisplayUtilities.DisplayJson("SendTeamMessage", response);
    }
}