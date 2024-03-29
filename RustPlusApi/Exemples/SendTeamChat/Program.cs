﻿using __Constants;

var rustPlus = new RustPlusApi.RustPlusApi(RustPlusConst.Ip, RustPlusConst.Port, RustPlusConst.PlayerId, RustPlusConst.PlayerToken);

rustPlus.Connected += async (sender, e) =>
{
    await rustPlus.SendTeamMessageAsync("Hello from RustPlusApi!");
    rustPlus.Dispose();
};

await rustPlus.ConnectAsync();