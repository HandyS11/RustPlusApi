using System.Text.Json;
using RustPlusApi;

using static Constants.ExamplesConst;

var rustPlus = new RustPlusLegacy(Ip, Port, PlayerId, PlayerToken);
const ulong steamId = 0;

await rustPlus.ConnectAsync();

var message = await rustPlus.PromoteToLeaderLegacyAsync(steamId);
Console.WriteLine($"Infos:\n{JsonSerializer.Serialize(message, JsonOptions)}");

await rustPlus.DisconnectAsync();