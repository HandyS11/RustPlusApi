using Newtonsoft.Json;

using RustPlusApi;
using RustPlusApi.Data;

using static __Constants.ExamplesConst;

var rustPlus = new RustPlus(Ip, Port, PlayerId, PlayerToken);

rustPlus.Connected += async (_, _) =>
{
    // The message would be a ServerMap
    // If you want to do your own parsing you can set the useRawObject parameter to true
    // (this code will obviously break if you do)
    var message = await rustPlus.GetMapAsync();

    if (message is not ServerMap) return;
    var serverMap = (ServerMap)message;

    File.WriteAllBytes("map.jpg", serverMap.JpgImage!);

    serverMap.JpgImage = null;
    Console.WriteLine($"Infos:\n{JsonConvert.SerializeObject(serverMap, JsonSettings)}");

    Console.WriteLine($"Image saved under: {Directory.GetCurrentDirectory()}");

    rustPlus.Dispose();
};

await rustPlus.ConnectAsync();