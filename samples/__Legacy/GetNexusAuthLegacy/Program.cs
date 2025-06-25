using System.Text.Json;
using RustPlusApi;

using static Constants.ExamplesConst;

var rustPlus = new RustPlusLegacy(Ip, Port, PlayerId, PlayerToken);
const string apiKey = "";

await rustPlus.ConnectAsync();

// This method is not fully integrated in Rust, so it will not work until the Nexus update is released.
var message = await rustPlus.GetNexusAuthLegacyAsync(apiKey);
Console.WriteLine($"Infos:\n{JsonSerializer.Serialize(message, JsonOptions)}");

await rustPlus.DisconnectAsync();