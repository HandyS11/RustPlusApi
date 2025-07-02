using RustPlus.ConsoleApp.Utils;
using RustPlusApi.Interfaces;

namespace RustPlus.ConsoleApp.Features;

public class GetMapMarkers(IRustPlus rustPlus)
{
    public async Task GetMapMarkersAsync()
    {
        var response = await rustPlus.GetMapMarkersAsync();
        DisplayUtilities.DisplayJson("MapMarkers", response);
    }
}