using RustPlusApi;

using static __Constants.ExamplesConst;

var rustPlus = new RustPlusLegacy(Ip, Port, PlayerId, PlayerToken);
const uint entityId = 0;

rustPlus.Connected += async (_, _) =>
{
    await rustPlus.StrobeEntityLegacyAsync(entityId);

    Console.WriteLine($"Strobed entity: {entityId}");

    rustPlus.Dispose();
};

await rustPlus.ConnectAsync();