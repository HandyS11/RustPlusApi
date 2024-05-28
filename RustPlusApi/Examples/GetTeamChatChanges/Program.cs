using Newtonsoft.Json;

using RustPlusApi;

using static __Constants.ExamplesConst;

var rustPlus = new RustPlus(Ip, Port, PlayerId, PlayerToken);

rustPlus.Connected += async (_, _) =>
{
    await rustPlus.GetTeamChatAsync(message =>
    {
        Console.WriteLine($"Infos:\n{JsonConvert.SerializeObject(message, JsonSettings)}");
        return true;
    });
};

rustPlus.MessageReceived += (_, message) =>
{
    if (message.Broadcast is not { TeamMessage: not null }) return;

    var teamMessage = message.Broadcast.TeamMessage;
    Console.WriteLine($"Message:\n{JsonConvert.SerializeObject(teamMessage, JsonSettings)}");
};

await rustPlus.ConnectAsync();