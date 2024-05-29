using Newtonsoft.Json;

using RustPlusApi;

using static __Constants.ExamplesConst;

var rustPlus = new RustPlus(Ip, Port, PlayerId, PlayerToken);
uint entityId = 0;

rustPlus.Connected += async (_, _) =>
{
    // The message would be SmartSwitchInfo, AlarmInfo, or StorageMonitorInfo
    // If you want to do your own parsing you can set the useRawObject parameter to true
    var message = await rustPlus.GetEntityInfoAsync(entityId);

    Console.WriteLine($"Infos:\n{JsonConvert.SerializeObject(message, JsonSettings)}");

    rustPlus.Dispose();
};

await rustPlus.ConnectAsync();