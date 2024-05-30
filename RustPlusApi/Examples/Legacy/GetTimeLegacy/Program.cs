using Newtonsoft.Json;

using RustPlusApi;

using static __Constants.ExamplesConst;

var rustPlus = new RustPlusLegacy(Ip, Port, PlayerId, PlayerToken);

rustPlus.Connected += async (_, _) =>
{
    var message = await rustPlus.GetTimeLegacyAsync();

    Console.WriteLine($"Infos:\n{JsonConvert.SerializeObject(message, JsonSettings)}");

    rustPlus.Dispose();
};

await rustPlus.ConnectAsync();