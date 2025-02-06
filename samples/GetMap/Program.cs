using Newtonsoft.Json;

using RustPlusApi;

using static __Constants.ExamplesConst;

var rustPlus = new RustPlus(Ip, Port, PlayerId, PlayerToken);

await rustPlus.ConnectAsync();

var message = await rustPlus.GetMapAsync();

if (!message.IsSuccess) return;

File.WriteAllBytes("map.jpg", message.Data?.JpgImage!);

message.Data!.JpgImage = null;
Console.WriteLine($"Infos:\n{JsonConvert.SerializeObject(message, JsonSettings)}");
Console.WriteLine($"Image saved under: {Directory.GetCurrentDirectory()}");

await rustPlus.DisconnectAsync();