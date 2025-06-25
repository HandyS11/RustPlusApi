using System.Text.Json;
using RustPlusApi;

using static Constants.ExamplesConst;

var rustPlus = new RustPlus(Ip, Port, PlayerId, PlayerToken);
const uint entityId = 0;

await rustPlus.ConnectAsync();

var message = await rustPlus.GetAlarmInfoAsync(entityId);
Console.WriteLine($"Infos:\n{JsonSerializer.Serialize(message, JsonOptions)}");

await rustPlus.DisconnectAsync();