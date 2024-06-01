using Newtonsoft.Json;

using RustPlusApi;

using static __Constants.ExamplesConst;

var rustPlus = new RustPlus(Ip, Port, PlayerId, PlayerToken);
const uint entityId = 85953;

await rustPlus.ConnectAsync();

var message = await rustPlus.GetAlarmInfoAsync(entityId);
Console.WriteLine($"Infos:\n{JsonConvert.SerializeObject(message, JsonSettings)}");

await rustPlus.DisconnectAsync();