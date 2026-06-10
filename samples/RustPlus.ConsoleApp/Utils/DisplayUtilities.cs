using RustPlusApi.Data;
using System.Text.Json;

namespace RustPlus.ConsoleApp.Utils;

internal static class DisplayUtilities
{
    public static void DisplayJson<T>(string title, Response<T> message)
    {
        Console.WriteLine(message.IsSuccess
            ? $"{title}:\n{JsonSerializer.Serialize(message, JsonUtilities.JsonOptions)}"
            : $"{title} failed: {message.Error?.Message}");
    }

    public static void DisplayJson(string title, Response message)
    {
        Console.WriteLine(message.IsSuccess
            ? $"{title}: success"
            : $"{title} failed: {message.Error?.Message}");
    }

    public static void DisplayEvent(string title, object message)
    {
        Console.WriteLine($"{title}:\n{JsonSerializer.Serialize(message, JsonUtilities.JsonOptions)}");
    }

    public static void DisplaySmartSwitchValue(ulong smartSwitchId, bool smartSwitchValue)
    {
        Console.WriteLine($"Smart switch: {smartSwitchId} is now {(smartSwitchValue ? "enable" : "disable")}!");
    }
}
