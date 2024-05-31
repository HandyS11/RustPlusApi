using RustPlusApi;

using static __Constants.ExamplesConst;

var rustPlus = new RustPlusLegacy(Ip, Port, PlayerId, PlayerToken);

await rustPlus.ConnectAsync();

var message = await rustPlus.GetMapLegacyAsync();

if (message.Response.Error is not null) return;

File.WriteAllBytes("map.jpg", message.Response.Map.JpgImage.ToByteArray());
Console.WriteLine($"Image saved under: {Directory.GetCurrentDirectory()}");

await rustPlus.DisconnectAsync();