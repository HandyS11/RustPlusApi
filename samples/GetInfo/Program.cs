using System.Text.Json;
using RustPlusApi;

using static Constants.ExamplesConst;

var rustPlus = new RustPlus(Ip, Port, PlayerId, PlayerToken);

await rustPlus.ConnectAsync();

var message = await rustPlus.GetInfoAsync();
Console.WriteLine($"Infos:\n{JsonSerializer.Serialize(message, JsonOptions)}");

await rustPlus.DisconnectAsync();