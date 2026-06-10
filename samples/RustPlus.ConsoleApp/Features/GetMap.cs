using RustPlusApi.Interfaces;

namespace RustPlus.ConsoleApp.Features;

internal sealed class GetMap(IRustPlus rustPlus)
{
    public async Task GetMapAsync()
    {
        var response = await rustPlus.GetMapAsync();
        if (!response.IsSuccess)
        {
            Console.WriteLine($"Map failed: {response.Error?.Message}");
            return;
        }

        var map = response.Data!;
        Console.WriteLine("Map:");
        Console.WriteLine($"  Size:        {map.Width?.ToString("D", System.Globalization.CultureInfo.InvariantCulture) ?? "?"} x {map.Height?.ToString("D", System.Globalization.CultureInfo.InvariantCulture) ?? "?"} game units");
        Console.WriteLine($"  Ocean margin: {map.OceanMargin?.ToString("D", System.Globalization.CultureInfo.InvariantCulture) ?? "?"}");
        Console.WriteLine($"  Monuments:   {map.Monuments?.Count ?? 0}");

        if (map.JpgImage is { Length: > 0 })
        {
            await File.WriteAllBytesAsync("map.jpg", map.JpgImage);
            Console.WriteLine($"  Image saved: {Path.Combine(Directory.GetCurrentDirectory(), "map.jpg")}");
        }
        else
        {
            Console.WriteLine("  Image:       (none returned)");
        }
    }
}
