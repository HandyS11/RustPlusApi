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
};

rustPlus.OnSmartSwitchTriggered += (_, message) =>
{
    Console.WriteLine($"SmartSwitch:\n{JsonConvert.SerializeObject(message, JsonSettings)}");
};

rustPlus.OnStorageMonitorTriggered += (_, message) =>
{
    Console.WriteLine($"StorageMonitor:\n{JsonConvert.SerializeObject(message, JsonSettings)}");
};

// If you want to get the raw message for the notification you can use the MessageReceived event
//rustPlus.MessageReceived += (_, message) =>
//{
//    if (message.Broadcast is not { EntityChanged: not null }) return;

//    var entityChanged = message.Broadcast.EntityChanged;
//    Console.WriteLine($"Message:\n{JsonConvert.SerializeObject(entityChanged, JsonSettings)}");
//};

await rustPlus.ConnectAsync();