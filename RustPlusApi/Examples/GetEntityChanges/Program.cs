using Newtonsoft.Json;

using RustPlusApi;

using static __Constants.ExamplesConst;

var rustPlus = new RustPlus(Ip, Port, PlayerId, PlayerToken);

rustPlus.Connected += async (_, _) =>
{
    await rustPlus.GetEntityInfoAsync(EntityId, message =>
    {
        Console.WriteLine($"Infos:\n{JsonConvert.SerializeObject(message, JsonSettings)}");
        return true;
    });
};

rustPlus.MessageReceived += (_, message) =>
{
    if (message.Broadcast is not { EntityChanged: not null }) return;

    var entityChanged = message.Broadcast.EntityChanged;
    Console.WriteLine($"Message:\n{JsonConvert.SerializeObject(entityChanged, JsonSettings)}");
};

await rustPlus.ConnectAsync();