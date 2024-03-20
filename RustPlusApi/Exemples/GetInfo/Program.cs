using __Constants;

var rustPlus = new RustPlusApi.RustPlusApi(RustPlusConst.Ip, RustPlusConst.Port, RustPlusConst.PlayerId, RustPlusConst.PlayerToken);

rustPlus.Connected += async (sender, e) =>
{
    await rustPlus.GetInfoAsync(message =>
    {
        Console.WriteLine($"Infos: " +
                          $"\nServer Name: {message.Response.Info.Name}" +
                          $"\nServer Url: {message.Response.Info.Url}");
        rustPlus.Dispose();
        return true;
    });
};

await rustPlus.ConnectAsync();