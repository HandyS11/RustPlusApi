using Newtonsoft.Json;

using RustPlusApi;

using static __Constants.ExamplesConst;

var rustPlus = new RustPlus(Ip, Port, PlayerId, PlayerToken);

rustPlus.Connected += async (_, _) =>
{
    var message = await rustPlus.GetInfoAsync();

    Console.WriteLine($"Infos:\n{JsonConvert.SerializeObject(message, JsonSettings)}");

    rustPlus.Dispose();
};

await rustPlus.ConnectAsync();