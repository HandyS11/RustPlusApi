using __Constants;

var rustPlus = new RustPlusApi.RustPlusApi(RustPlusConst.Ip, RustPlusConst.Port, RustPlusConst.PlayerId, RustPlusConst.PlayerToken);

rustPlus.Connected += async (sender, e) =>
{
    await rustPlus.GetMapAsync(message =>
    {
        var imageData = message.Response.Map.JpgImage.ToByteArray();
        File.WriteAllBytes("map.jpg", imageData);
        rustPlus.Dispose();
        return true;
    });
};

await rustPlus.ConnectAsync();