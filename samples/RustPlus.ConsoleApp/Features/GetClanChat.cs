using RustPlus.ConsoleApp.Utils;
using RustPlusApi.Interfaces;

namespace RustPlus.ConsoleApp.Features;

internal sealed class GetClanChat(IRustPlus rustPlus)
{
    public async Task GetClanChatAsync()
    {
        var response = await rustPlus.GetClanChatAsync();
        DisplayUtilities.DisplayJson("ClanChat", response);
    }
}
