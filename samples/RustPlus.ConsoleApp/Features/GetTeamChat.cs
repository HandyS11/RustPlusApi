using RustPlus.ConsoleApp.Utils;
using RustPlusApi.Interfaces;

namespace RustPlus.ConsoleApp.Features;

internal sealed class GetTeamChat(IRustPlus rustPlus)
{
    public async Task GetTeamChatAsync()
    {
        var response = await rustPlus.GetTeamChatAsync();
        DisplayUtilities.DisplayJson("TeamChat", response);
    }
}
