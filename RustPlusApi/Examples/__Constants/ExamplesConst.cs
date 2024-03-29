using Newtonsoft.Json;

namespace __Constants
{
    public record ExamplesConst
    {
        public const string Ip = "";
        public const int Port = 0;
        public const ulong PlayerId = 0;
        public const int PlayerToken = 0;

        public const int EntityId = 0;
        public const bool EntityValue = true;

        public static JsonSerializerSettings JsonSettings = new()
        {
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.Indented,
        };
    }
}
