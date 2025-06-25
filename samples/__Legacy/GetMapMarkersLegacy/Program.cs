using System.Text.Json;
using RustPlusApi;

using static Constants.ExamplesConst;

var rustPlus = new RustPlusLegacy(Ip, Port, PlayerId, PlayerToken);

await rustPlus.ConnectAsync();

var message = await rustPlus.GetMapMarkersLegacyAsync();
Console.WriteLine($"Infos:\n{JsonSerializer.Serialize(message, JsonOptions)}");

await rustPlus.DisconnectAsync();