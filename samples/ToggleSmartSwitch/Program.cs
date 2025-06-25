using System.Text.Json;
using RustPlusApi;

using static Constants.ExamplesConst;

var rustPlus = new RustPlus(Ip, Port, PlayerId, PlayerToken);
const uint smartSwitchId = 0;

await rustPlus.ConnectAsync();

var message = await rustPlus.ToggleSmartSwitchAsync(smartSwitchId);
Console.WriteLine($"Infos:\n{JsonSerializer.Serialize(message, JsonOptions)}");

if (message.IsSuccess)
    Console.WriteLine($"Smart switch: {smartSwitchId} is now {(message.Data!.IsActive ? "enable" : "disable")}!");

await rustPlus.DisconnectAsync();