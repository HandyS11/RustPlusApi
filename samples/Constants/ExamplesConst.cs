using System.Text.Json;
using System.Text.Json.Serialization;

namespace Constants;

// ReSharper disable once ClassNeverInstantiated.Global
public sealed record ExamplesConst
{
    public const string Ip = "";
    public const int Port = 0;
    public const ulong PlayerId = 0;
    public const int PlayerToken = 0;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };
}
