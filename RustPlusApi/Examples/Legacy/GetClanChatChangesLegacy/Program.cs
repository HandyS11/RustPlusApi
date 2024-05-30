using Newtonsoft.Json;

using RustPlusApi;

using static __Constants.ExamplesConst;

var rustPlus = new RustPlusLegacy(Ip, Port, PlayerId, PlayerToken);

rustPlus.Connected += async (_, _) =>
{
    // This method is not fully integrated in Rust so it will not work until the Clan update is released.
    var message = await rustPlus.GetClanChatLegacyAsync();

    Console.WriteLine($"Infos:\n{JsonConvert.SerializeObject(message, JsonSettings)}");
};

rustPlus.MessageReceived += (_, message) =>
{
    if (message.Broadcast is not { ClanChanged: not null }) return;

    Console.WriteLine($"Message:\n{JsonConvert.SerializeObject(message, JsonSettings)}");
};

await rustPlus.ConnectAsync();