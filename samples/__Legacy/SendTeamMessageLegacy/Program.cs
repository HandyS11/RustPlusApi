using Newtonsoft.Json;

using RustPlusApi;

using static __Constants.ExamplesConst;

var rustPlus = new RustPlusLegacy(Ip, Port, PlayerId, PlayerToken);
const string teamMessage = "Hello world!";

await rustPlus.ConnectAsync();

var message = await rustPlus.SendTeamMessageLegacyAsync(teamMessage);
Console.WriteLine($"Infos:\n{JsonConvert.SerializeObject(message, JsonSettings)}");

await rustPlus.DisconnectAsync();