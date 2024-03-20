using __Constants;

var rustPlus = new RustPlusApi.RustPlusApi(RustPlusConst.Ip, RustPlusConst.Port, RustPlusConst.PlayerId, RustPlusConst.PlayerToken);

rustPlus.Connected += async (sender, e) =>
{
    await rustPlus.GetTimeAsync(message =>
    {
        Console.WriteLine($"Time: {message.Response.Time.Time}");
        rustPlus.Dispose();
        return true;
    });
};

await rustPlus.ConnectAsync();