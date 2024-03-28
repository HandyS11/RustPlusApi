using Newtonsoft.Json;

namespace __Constants
{
    public record RustPlusConst
    {
        public const string Ip = "138.201.18.133";
        public const int Port = 28082;
        public const ulong PlayerId = 76561198249527954;
        public const int PlayerToken = 263698233;

        public static JsonSerializerSettings JsonSettings = new()
        {
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.Indented,
        };
    }
}
