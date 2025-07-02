using RustPlus.ConsoleApp.Utils;
using RustPlusApi.Interfaces;

namespace RustPlus.ConsoleApp.Features;

public class GetMap(IRustPlus rustPlus)
{
    public async Task GetMapAsync()
    {
        var response = await rustPlus.GetMapAsync();

        if (!response.IsSuccess) return;
        await File.WriteAllBytesAsync("map.jpg", response.Data?.JpgImage!);
        
        DisplayUtilities.DisplayJson("Map", response);
        Console.WriteLine($"Image saved under: {Directory.GetCurrentDirectory()}");
    }
}