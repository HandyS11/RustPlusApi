const string ip = "";
const int port = 0;
const ulong playerId = 0;
const int playerToken = 0;

var rustPlus = new RustPlusApi.RustPlusApi(ip, port, playerId, playerToken);

rustPlus.Connected += async (sender, e) =>
{
    await rustPlus.SendTeamMessageAsync("Hello from RustPlusApi!");
    rustPlus.Dispose();
};

await rustPlus.ConnectAsync();