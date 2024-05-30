using Newtonsoft.Json;

using RustPlusApi;

using static __Constants.ExamplesConst;

var rustPlus = new RustPlusLegacy(Ip, Port, PlayerId, PlayerToken);
const uint entityId = 0;

rustPlus.Connected += async (_, _) =>
{
    var message = await rustPlus.GetEntityInfoLegacyAsync(entityId);

    Console.WriteLine($"Infos:\n{JsonConvert.SerializeObject(message, JsonSettings)}");
};

rustPlus.MessageReceived += (_, message) =>
{
    if (message.Broadcast is not { EntityChanged: not null }) return;

    var entityChanged = message.Broadcast.EntityChanged;
    Console.WriteLine($"Message:\n{JsonConvert.SerializeObject(entityChanged, JsonSettings)}");
};

await rustPlus.ConnectAsync();