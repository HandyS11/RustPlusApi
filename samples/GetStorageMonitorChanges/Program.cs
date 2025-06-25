using System.Text.Json;
using RustPlusApi;

using static Constants.ExamplesConst;

var rustPlus = new RustPlus(Ip, Port, PlayerId, PlayerToken);
const uint entityId = 0;

rustPlus.OnStorageMonitorTriggered += (_, message) =>
{
    Console.WriteLine($"StorageMonitor:\n{JsonSerializer.Serialize(message, JsonOptions)}");
};

await rustPlus.ConnectAsync();

var message = await rustPlus.GetStorageMonitorInfoAsync(entityId);
Console.WriteLine($"Infos:\n{JsonSerializer.Serialize(message, JsonOptions)}");

Console.ReadLine();
await rustPlus.DisconnectAsync();