using Newtonsoft.Json;

using RustPlusApi;

using static __Constants.ExamplesConst;

var rustPlus = new RustPlus(Ip, Port, PlayerId, PlayerToken);
var entityId = 0;
var entityValue = true;

rustPlus.Connected += async (_, _) =>
{
    await rustPlus.SetEntityValueAsync(entityId, entityValue, message =>
    {
        Console.WriteLine($"Infos:\n{JsonConvert.SerializeObject(message, JsonSettings)}");
        rustPlus.Dispose();
        return true;
    });
};

await rustPlus.ConnectAsync();