using RustPlusApi;

using static __Constants.ExamplesConst;

var rustPlus = new RustPlus(Ip, Port, PlayerId, PlayerToken);
const uint smartSwitchId = 0;
var smartSwitchValue = true;

await rustPlus.ConnectAsync();

await rustPlus.SetSmartSwitchValue(smartSwitchId, smartSwitchValue);
Console.WriteLine($"Smart switch: {smartSwitchId} is now {(smartSwitchValue ? "enabled" : "disabled")}!");

await rustPlus.DisconnectAsync();