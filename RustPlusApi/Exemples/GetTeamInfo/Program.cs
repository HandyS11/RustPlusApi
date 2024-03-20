using __Constants;

var rustPlus = new RustPlusApi.RustPlusApi(RustPlusConst.Ip, RustPlusConst.Port, RustPlusConst.PlayerId, RustPlusConst.PlayerToken);

rustPlus.Connected += async (sender, e) =>
{
    await rustPlus.GetTeamInfoAsync(message =>
    {
        Console.WriteLine($"Infos:" +
                          $"\nLeader SteamId: {message.Response.TeamInfo.LeaderSteamId}" +
                          $"\nMembers Count: {message.Response.TeamInfo.Members.Count}");
        rustPlus.Dispose();
        return true;
    });
};

await rustPlus.ConnectAsync();