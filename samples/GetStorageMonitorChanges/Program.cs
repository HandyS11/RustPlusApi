using Newtonsoft.Json;

using RustPlusApi;

using static __Constants.ExamplesConst;

var rustPlus = new RustPlus(Ip, Port, PlayerId, PlayerToken);
const uint entityId = 0;

rustPlus.OnStorageMonitorTriggered += (_, message) =>
{
    Console.WriteLine($"StorageMonitor:\n{JsonConvert.SerializeObject(message, JsonSettings)}");
};

await rustPlus.ConnectAsync();

var message = await rustPlus.GetStorageMonitorInfoAsync(entityId);
Console.WriteLine($"Infos:\n{JsonConvert.SerializeObject(message, JsonSettings)}");

Console.ReadLine();
await rustPlus.DisconnectAsync();