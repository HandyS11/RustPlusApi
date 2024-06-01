using RustPlusApi;

using static __Constants.ExamplesConst;

var rustPlus = new RustPlus(Ip, Port, PlayerId, PlayerToken);

await rustPlus.ConnectAsync();

await rustPlus.SendTeamMessageAsync("Hello from RustPlusApi!");
Console.WriteLine("Message sent in-game!");

await rustPlus.DisconnectAsync();