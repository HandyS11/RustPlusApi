﻿using Newtonsoft.Json;

using RustPlusApi;

using static __Constants.ExamplesConst;

var rustPlus = new RustPlus(Ip, Port, PlayerId, PlayerToken);
ulong steamId = 0;

rustPlus.Connected += async (_, _) =>
{
    await rustPlus.PromoteToLeaderAsync(steamId, message =>
    {
        Console.WriteLine($"Infos:\n{JsonConvert.SerializeObject(message, JsonSettings)}");
        rustPlus.Dispose();
        return true;
    });
};

await rustPlus.ConnectAsync();