using System.Text.Json.Serialization;
using RustPlusApi.Fcm.Converters;

namespace RustPlusApi.Fcm.Data;

public sealed record FcmMessage
{
    public string PersistantId { get; init; } = null!;
    public long From { get; init; }
    public DateTime SentAt { get; init; }
    public MessageData Data { get; init; } = null!;
}

public sealed record MessageData
{
    public Guid ProjectId { get; init; }
    public string ChannelId { get; init; } = null!;
    public string Title { get; init; } = null!;
    public string Message { get; init; } = null!;
    public string ExperienceId { get; init; } = null!;
    public string ScopeKey { get; init; } = null!;
    public Body Body { get; init; } = null!;
}

public sealed record Body
{
    public Guid Id { get; init; }
    public string Ip { get; init; } = null!;
    [JsonConverter(typeof(Int32StringConverter))]
    public int Port { get; init; }
    public string Name { get; init; } = null!;
    public string? Desc { get; init; }
    public string? Logo { get; init; }
    public string? Img { get; init; }
    public string? Url { get; init; }
    [JsonConverter(typeof(StringToUInt64Converter))]
    public ulong PlayerId { get; init; }
    public string PlayerToken { get; init; } = null!;
    public string Type { get; init; } = null!;
    [JsonConverter(typeof(Int32StringConverter))]
    public int? EntityType { get; init; }
    [JsonConverter(typeof(Int32StringConverter))]
    public int? EntityId { get; init; }
    public string? EntityName { get; init; }
}