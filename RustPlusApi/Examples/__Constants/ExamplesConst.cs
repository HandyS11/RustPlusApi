using Newtonsoft.Json;
// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable FieldCanBeMadeReadOnly.Global

namespace __Constants
{
    public record ExamplesConst
    {
        public const string Ip = "";
        public const int Port = 0;
        public const ulong PlayerId = 0;
        public const int PlayerToken = 0;

        public static JsonSerializerSettings JsonSettings = new()
        {
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.Indented,
        };
    }
}
