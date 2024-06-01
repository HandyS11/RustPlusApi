﻿using RustPlusApi;

using static __Constants.ExamplesConst;

var rustPlus = new RustPlus(Ip, Port, PlayerId, PlayerToken);
const ulong steamId = 0;

await rustPlus.ConnectAsync();

await rustPlus.PromoteToLeaderAsync(steamId);
Console.WriteLine($"Player: {steamId} is now the team leader!");

await rustPlus.DisconnectAsync();