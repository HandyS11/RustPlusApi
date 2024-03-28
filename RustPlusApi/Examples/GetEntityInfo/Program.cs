using Newtonsoft.Json;

using RustPlusApi;

using static __Constants.RustPlusConst;

const int entityId = 232773;
var rustPlus = new RustPlus(Ip, Port, PlayerId, PlayerToken);

rustPlus.Connected += async (_, _) =>
{
    await rustPlus.GetEntityInfoAsync(entityId, message =>
    {
        Console.WriteLine($"Infos:\n{JsonConvert.SerializeObject(message, JsonSettings)}");
        rustPlus.Dispose();
        return true;
    });
};

await rustPlus.ConnectAsync();