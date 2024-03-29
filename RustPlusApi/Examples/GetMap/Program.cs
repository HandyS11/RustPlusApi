using RustPlusApi;

using static __Constants.ExamplesConst;

var rustPlus = new RustPlus(Ip, Port, PlayerId, PlayerToken);

rustPlus.Connected += async (_, _) =>
{
    await rustPlus.GetMapAsync(message =>
    {
        var imageData = message.Response.Map.JpgImage.ToByteArray();
        if (imageData == null) return false;
        File.WriteAllBytes("map.jpg", imageData);
        Console.WriteLine(@"Saved under .\RustPlusApi\RustPlusApi\Examples\GetMap\bin\Debug\net8.0");
        rustPlus.Dispose();
        return true;
    });
};

await rustPlus.ConnectAsync();