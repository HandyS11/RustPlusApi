using System.Text.Json.Serialization;

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

// Rust+ encodes the numeric fields below as JSON strings; STJ's number handling reads them
// from strings (and writes them back as strings) natively, so no custom converters are needed.
[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
public sealed record Body
{
    public Guid Id { get; init; }
    public string Ip { get; init; } = null!;
    public int Port { get; init; }
    public string Name { get; init; } = null!;
    public string? Desc { get; init; }
    public string? Logo { get; init; }
    public string? Img { get; init; }
    public string? Url { get; init; }
    public ulong PlayerId { get; init; }
    public string PlayerToken { get; init; } = null!;
    public string Type { get; init; } = null!;
    public int? EntityType { get; init; }
    public int? EntityId { get; init; }
    public string? EntityName { get; init; }
}