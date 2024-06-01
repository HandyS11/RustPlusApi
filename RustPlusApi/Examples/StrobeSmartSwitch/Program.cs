using RustPlusApi;

using static __Constants.ExamplesConst;

var rustPlus = new RustPlus(Ip, Port, PlayerId, PlayerToken);
const uint smartSwitchId = 0;

await rustPlus.ConnectAsync();

await rustPlus.StrobeSmartSwitchAsync(smartSwitchId);
Console.WriteLine($"Smart switch: {smartSwitchId} have been strobe!");

await rustPlus.DisconnectAsync();