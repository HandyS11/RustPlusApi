using System.Text.Json;
using RustPlusApi;

using static Constants.ExamplesConst;

var rustPlus = new RustPlus(Ip, Port, PlayerId, PlayerToken);
const ulong steamId = 0;

await rustPlus.ConnectAsync();

var message = await rustPlus.PromoteToLeaderAsync(steamId);
Console.WriteLine($"Infos:\n{JsonSerializer.Serialize(message, JsonOptions)}");

await rustPlus.DisconnectAsync();