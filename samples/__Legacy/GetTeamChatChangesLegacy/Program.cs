using System.Text.Json;
using RustPlusApi;

using static Constants.ExamplesConst;

var rustPlus = new RustPlusLegacy(Ip, Port, PlayerId, PlayerToken);

rustPlus.NotificationReceived += (_, message) =>
{
    if (message.Broadcast is not { TeamMessage: not null }) return;

    Console.WriteLine($"Message:\n{JsonSerializer.Serialize(message, JsonOptions)}");
};

await rustPlus.ConnectAsync();

var message = await rustPlus.GetTeamChatLegacyAsync();
Console.WriteLine($"Infos:\n{JsonSerializer.Serialize(message, JsonOptions)}");

Console.ReadLine();
await rustPlus.DisconnectAsync();