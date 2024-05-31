using Newtonsoft.Json;

using RustPlusApi;

using static __Constants.ExamplesConst;

var rustPlus = new RustPlusLegacy(Ip, Port, PlayerId, PlayerToken);

await rustPlus.ConnectAsync();

var message = await rustPlus.GetMapMarkersLegacyAsync();
Console.WriteLine($"Infos:\n{JsonConvert.SerializeObject(message, JsonSettings)}");

await rustPlus.DisconnectAsync();