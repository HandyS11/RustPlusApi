using RustPlusApi;

using static Constants.ExamplesConst;

var rustPlus = new RustPlusLegacy(Ip, Port, PlayerId, PlayerToken);
const uint entityId = 0;

await rustPlus.ConnectAsync();

await rustPlus.ToggleEntityValueLegacyAsync(entityId);
Console.WriteLine($"Toggled entity: {entityId}");

await rustPlus.DisconnectAsync();