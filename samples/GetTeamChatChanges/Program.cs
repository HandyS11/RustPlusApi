using System.Text.Json;
using RustPlusApi;

using static Constants.ExamplesConst;

var rustPlus = new RustPlus(Ip, Port, PlayerId, PlayerToken);

rustPlus.OnTeamChatReceived += (_, message) =>
{
    Console.WriteLine($"Infos:\n{JsonSerializer.Serialize(message, JsonOptions)}");
};

await rustPlus.ConnectAsync();

var message = await rustPlus.GetTeamChatAsync();
Console.WriteLine($"Infos:\n{JsonSerializer.Serialize(message, JsonOptions)}");

Console.ReadLine();
await rustPlus.DisconnectAsync();
