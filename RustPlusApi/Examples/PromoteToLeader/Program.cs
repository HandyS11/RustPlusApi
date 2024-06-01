using Newtonsoft.Json;

using RustPlusApi;

using static __Constants.ExamplesConst;

var rustPlus = new RustPlus(Ip, Port, PlayerId, PlayerToken);
const ulong steamId = 0;

await rustPlus.ConnectAsync();

var message = await rustPlus.PromoteToLeaderAsync(steamId);
Console.WriteLine($"Infos:\n{JsonConvert.SerializeObject(message, JsonSettings)}");

await rustPlus.DisconnectAsync();