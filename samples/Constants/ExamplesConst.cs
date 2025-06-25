using System.Text.Json;
using System.Text.Json.Serialization;

namespace Constants;

// ReSharper disable once ClassNeverInstantiated.Global
public sealed record ExamplesConst
{
    public const string Ip = "137.83.91.158";
    public const int Port = 28086;
    public const ulong PlayerId = 76561198249527954;
    public const int PlayerToken = 734549542;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };
}
