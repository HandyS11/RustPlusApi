using System.Text.Json;
using RustPlusApi;

using static Constants.ExamplesConst;

var rustPlus = new RustPlus(Ip, Port, PlayerId, PlayerToken);
const uint smartSwitchId = 0;
const bool smartSwitchValue = true;

await rustPlus.ConnectAsync();

var message = await rustPlus.SetSubscriptionAsync(smartSwitchId, smartSwitchValue);
Console.WriteLine($"Infos:\n{JsonSerializer.Serialize(message, JsonOptions)}");

if (message.IsSuccess)
    Console.WriteLine($"Smart switch: {smartSwitchId} have its notifications {(smartSwitchValue ? "enable" : "disable")}!");

await rustPlus.DisconnectAsync();