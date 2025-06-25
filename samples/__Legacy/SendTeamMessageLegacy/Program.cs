using System.Text.Json;
using RustPlusApi;

using static Constants.ExamplesConst;

var rustPlus = new RustPlusLegacy(Ip, Port, PlayerId, PlayerToken);
const string teamMessage = "Hello world!";

await rustPlus.ConnectAsync();

var message = await rustPlus.SendTeamMessageLegacyAsync(teamMessage);
Console.WriteLine($"Infos:\n{JsonSerializer.Serialize(message, JsonOptions)}");

await rustPlus.DisconnectAsync();