using Newtonsoft.Json;

using RustPlusApi;

using static __Constants.ExamplesConst;

var rustPlus = new RustPlus(Ip, Port, PlayerId, PlayerToken);
const uint smartSwitchId = 0;
const bool smartSwitchValue = true;

await rustPlus.ConnectAsync();

var message = await rustPlus.SetSmartSwitchValue(smartSwitchId, smartSwitchValue);
Console.WriteLine($"Infos:\n{JsonConvert.SerializeObject(message, JsonSettings)}");

if (message.IsSuccess)
    Console.WriteLine($"Smart switch: {smartSwitchId} is now {(smartSwitchValue ? "enable" : "disable")}!");

await rustPlus.DisconnectAsync();