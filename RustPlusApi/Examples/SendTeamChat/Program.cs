using RustPlusApi;

using static __Constants.ExamplesConst;

var rustPlus = new RustPlus(Ip, Port, PlayerId, PlayerToken);

rustPlus.Connected += async (_, _) =>
{
    await rustPlus.SendTeamMessageAsync("Hello from RustPlusApi!");
    rustPlus.Dispose();
};

await rustPlus.ConnectAsync();