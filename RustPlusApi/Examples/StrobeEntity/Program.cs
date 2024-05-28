using RustPlusApi;

using static __Constants.ExamplesConst;

var rustPlus = new RustPlus(Ip, Port, PlayerId, PlayerToken);
var entityId = 0;

rustPlus.Connected += async (_, _) =>
{
    await rustPlus.StrobeAsync(entityId);
    rustPlus.Dispose();
};

await rustPlus.ConnectAsync();