using Newtonsoft.Json;

using RustPlusApi;

using static __Constants.RustPlusConst;

var rustPlus = new RustPlus(Ip, Port, PlayerId, PlayerToken);

rustPlus.Connected += async (_, _) =>
{
    await rustPlus.GetInfoAsync(message =>
    {
        Console.WriteLine($"Infos:\n{JsonConvert.SerializeObject(message, JsonSettings)}");
        rustPlus.Dispose();
        return true;
    });
};

await rustPlus.ConnectAsync();