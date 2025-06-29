using RustPlus.ConsoleApp.Utils;
using RustPlusApi.Interfaces;

namespace RustPlus.ConsoleApp.Features;

public class SentTeamMessage(IRustPlus rustPlus)
{
    public async Task SendTeamMessageAsync(string message)
    {
        var response = await rustPlus.SendTeamMessageAsync(message);
        DisplayUtilities.DisplayJson("SendTeamMessage", response);
    }
}