using Newtonsoft.Json;

using RustPlusApi;

using static __Constants.ExamplesConst;

var rustPlus = new RustPlus(Ip, Port, PlayerId, PlayerToken);
const uint smartSwitchId = 0;
var smartSwitchValue = true;

await rustPlus.ConnectAsync();

var message = await rustPlus.SetSubscriptionAsync(smartSwitchId, smartSwitchValue);
Console.WriteLine($"Infos:\n{JsonConvert.SerializeObject(message, JsonSettings)}");

if (message.IsSuccess)
    Console.WriteLine($"Smart switch: {smartSwitchId} have its notifications {(smartSwitchValue ? "enable" : "disable")}!");

await rustPlus.DisconnectAsync();