using System.Text.Json;
using System.Text.Json.Serialization;

namespace RustPlus.ConsoleApp.Utils;

public static class JsonUtilities
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public static readonly JsonSerializerOptions JsonConfigOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}