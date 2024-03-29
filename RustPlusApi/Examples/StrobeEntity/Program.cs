using RustPlusApi;

using static __Constants.ExamplesConst;

var rustPlus = new RustPlus(Ip, Port, PlayerId, PlayerToken);

rustPlus.Connected += async (_, _) =>
{
    await rustPlus.StrobeAsync(EntityId);
    rustPlus.Dispose();
};

await rustPlus.ConnectAsync();