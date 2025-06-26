using System.Text.Json;
using RustPlusApi;

using static Constants.ExamplesConst;

var rustPlus = new RustPlus(Ip, Port, PlayerId, PlayerToken);

await rustPlus.ConnectAsync();

var message = await rustPlus.GetMapAsync();

if (!message.IsSuccess) return;
File.WriteAllBytes("map.jpg", message.Data?.JpgImage!);

Console.WriteLine($"Infos:\n{JsonSerializer.Serialize(message, JsonOptions)}");
Console.WriteLine($"Image saved under: {Directory.GetCurrentDirectory()}");

await rustPlus.DisconnectAsync();