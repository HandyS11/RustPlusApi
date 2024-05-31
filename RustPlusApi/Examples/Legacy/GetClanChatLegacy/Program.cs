﻿using Newtonsoft.Json;

using RustPlusApi;

using static __Constants.ExamplesConst;

var rustPlus = new RustPlusLegacy(Ip, Port, PlayerId, PlayerToken);


await rustPlus.ConnectAsync();

// This method is not fully integrated in Rust, so it will not work until the Clan update is released.
var message = await rustPlus.GetClanChatLegacyAsync();
Console.WriteLine($"Infos:\n{JsonConvert.SerializeObject(message, JsonSettings)}");

await rustPlus.DisconnectAsync();