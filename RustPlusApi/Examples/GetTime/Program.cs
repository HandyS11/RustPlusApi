using RustPlusApi;

using static __Constants.RustPlusConst;

var rustPlus = new RustPlus(Ip, Port, PlayerId, PlayerToken);

rustPlus.Connected += async (_, _) =>
{
    await rustPlus.GetTimeAsync(message =>
    {
        Console.WriteLine($"Time: {message.Response.Time.Time}");
        rustPlus.Dispose();
        return true;
    });
};

await rustPlus.ConnectAsync();