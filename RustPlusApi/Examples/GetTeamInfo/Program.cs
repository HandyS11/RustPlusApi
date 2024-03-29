using RustPlusApi;

using static __Constants.RustPlusConst;

var rustPlus = new RustPlus(Ip, Port, PlayerId, PlayerToken);

rustPlus.Connected += async (_, _) =>
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