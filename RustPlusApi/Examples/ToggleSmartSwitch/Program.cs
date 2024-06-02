using Newtonsoft.Json;

using RustPlusApi;

using static __Constants.ExamplesConst;

var rustPlus = new RustPlus(Ip, Port, PlayerId, PlayerToken);
const uint smartSwitchId = 0;

await rustPlus.ConnectAsync();

var message = await rustPlus.ToggleSmartSwitchAsync(smartSwitchId);
Console.WriteLine($"Infos:\n{JsonConvert.SerializeObject(message, JsonSettings)}");

if (message.IsSuccess)
    Console.WriteLine($"Smart switch: {smartSwitchId} is now {(message.Data!.IsActive ? "enable" : "disable")}!");

await rustPlus.DisconnectAsync();