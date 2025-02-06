using Newtonsoft.Json;

using RustPlusApi;

using static __Constants.ExamplesConst;

var rustPlus = new RustPlusLegacy(Ip, Port, PlayerId, PlayerToken);

rustPlus.NotificationReceived += (_, message) =>
{
    if (message.Broadcast is not { TeamMessage: not null }) return;

    Console.WriteLine($"Message:\n{JsonConvert.SerializeObject(message, JsonSettings)}");
};

await rustPlus.ConnectAsync();

var message = await rustPlus.GetTeamChatLegacyAsync();
Console.WriteLine($"Infos:\n{JsonConvert.SerializeObject(message, JsonSettings)}");

Console.ReadLine();
await rustPlus.DisconnectAsync();