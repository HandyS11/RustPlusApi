using Newtonsoft.Json;

using RustPlusApi.Fcm.Converters;

namespace RustPlusApi.Fcm.Data
{
    public class RustPlusMessage
    {
        public Data Data { get; set; } = null!;
        public string From { get; set; } = null!;
        public string Priority { get; set; } = null!;
        public string FcmMessageId { get; set; } = null!;
    }

    public class Data
    {
        public string ExperienceId { get; set; } = null!;
        public string ScopeKey { get; set; } = null!;
        [JsonConverter(typeof(BodyConverter))]
        public Body Body { get; set; } = null!;
        public string Message { get; set; } = null!;
        public string Title { get; set; } = null!;
        public string ProjectId { get; set; } = null!;
        public string ChannelId { get; set; } = null!;
    }

    public class Body
    {
        public string Img { get; set; } = null!;
        public string EntityType { get; set; } = null!;
        public string Ip { get; set; } = null!;
        public string EntityId { get; set; } = null!;
        public string Type { get; set; } = null!;
        public string Url { get; set; } = null!;
        public string PlayerToken { get; set; } = null!;
        public string Port { get; set; } = null!;
        public string EntityName { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string Logo { get; set; } = null!;
        public string Id { get; set; } = null!;
        public string Desc { get; set; } = null!;
        public string PlayerId { get; set; } = null!;
    }
}
