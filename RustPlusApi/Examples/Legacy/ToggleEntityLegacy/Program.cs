using RustPlusApi;

using static __Constants.ExamplesConst;

var rustPlus = new RustPlusLegacy(Ip, Port, PlayerId, PlayerToken);
const uint entityId = 85942;

rustPlus.Connected += async (_, _) =>
{
    await rustPlus.ToogleEntityValueLegacyAsync(entityId);

    Console.WriteLine($"Toggled entity: {entityId}");

    rustPlus.Dispose();
};

await rustPlus.ConnectAsync();