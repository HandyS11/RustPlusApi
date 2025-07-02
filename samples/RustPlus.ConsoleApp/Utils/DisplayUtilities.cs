using System.Text.Json;
using RustPlusApi.Data;

namespace RustPlus.ConsoleApp.Utils;

public static class DisplayUtilities
{
    public static void DisplayJson<T>(string title, Response<T> message)
    {
        Console.WriteLine(message.IsSuccess
            ? $"{title}:\n{JsonSerializer.Serialize(message, JsonUtilities.JsonOptions)}"
            : $"{title} failed: {message.Error?.Message}");
    }
    
    public static void DisplayEvent(string title, object message)
    {
        Console.WriteLine($"{title}:\n{JsonSerializer.Serialize(message, JsonUtilities.JsonOptions)}");
    }
    
    public static void DisplaySmartSwitchValue(uint smartSwitchId, bool smartSwitchValue)
    {
        Console.WriteLine($"Smart switch: {smartSwitchId} is now {(smartSwitchValue ? "enable" : "disable")}!");
    }
}