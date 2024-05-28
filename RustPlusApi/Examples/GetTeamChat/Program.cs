using Newtonsoft.Json;

using RustPlusApi;

using static __Constants.ExamplesConst;

var rustPlus = new RustPlus(Ip, Port, PlayerId, PlayerToken);

rustPlus.Connected += async (_, _) =>
{
    await rustPlus.GetTeamChatAsync(message =>
    {
        Console.WriteLine($"Infos:\n{JsonConvert.SerializeObject(message, JsonSettings)}");
        return true;
    });
};

await rustPlus.ConnectAsync();