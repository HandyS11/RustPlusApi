using RustPlus.ConsoleApp.Utils;
using RustPlusApi.Interfaces;

namespace RustPlus.ConsoleApp.Features;

public class GetTeamChat(IRustPlus rustPlus)
{
    public async Task GetTeamChatAsync()
    {
        var response = await rustPlus.GetTeamChatAsync();
        DisplayUtilities.DisplayJson("TeamChat", response);
    }
}