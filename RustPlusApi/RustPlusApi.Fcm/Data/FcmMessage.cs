using Newtonsoft.Json;

using RustPlusApi.Fcm.Converters;

namespace RustPlusApi.Fcm.Data
{
    public class FcmMessage
    {
        public Guid FcmMessageId { get; set; }
        public string Priority { get; set; } = null!;
        public long From { get; set; }
        public MessageData Data { get; set; } = null!;
    }

    public class MessageData
    {
        public Guid ProjectId { get; set; }
        public string ChannelId { get; set; } = null!;
        public string Title { get; set; } = null!;
        public string Message { get; set; } = null!;
        public string ExperienceId { get; set; } = null!;
        public string ScopeKey { get; set; } = null!;
        [JsonConverter(typeof(BodyConverter))]
        public Body Body { get; set; } = null!;
    }

    public class Body
    {
        public Guid Id { get; set; }
        public string Ip { get; set; } = null!;
        public int Port { get; set; }
        public string Name { get; set; } = null!;
        public string? Desc { get; set; }
        public string? Logo { get; set; }
        public string? Img { get; set; }
        public string? Url { get; set; }
        public ulong PlayerId { get; set; }
        public string PlayerToken { get; set; } = null!;
        public string Type { get; set; } = null!;
        public int? EntityType { get; set; }
        public int? EntityId { get; set; }
        public string EntityName { get; set; } = null!;
    }
}
