using System.Text.Json.Serialization;

namespace RustPlusApi.Fcm.Data;

/// <summary>A fully parsed FCM push notification as delivered by the MCS socket.</summary>
public sealed record FcmMessage
{
    /// <summary>FCM persistent ID used to de-duplicate already-processed messages.</summary>
    public string PersistentId { get; init; } = null!;

    /// <summary>FCM sender ID (the GCP project number that sent the message).</summary>
    public long From { get; init; }

    /// <summary>UTC timestamp when the message was sent by the FCM server.</summary>
    public DateTime SentAt { get; init; }

    /// <summary>The parsed notification payload.</summary>
    public MessageData Data { get; init; } = null!;
}

/// <summary>Metadata and structured body of a Rust+ FCM push notification.</summary>
public sealed record MessageData
{
    /// <summary>Expo project GUID that routed this notification.</summary>
    public Guid ProjectId { get; init; }

    /// <summary>Notification channel — e.g. <c>"pairing"</c> or <c>"alarm"</c>.</summary>
    public string ChannelId { get; init; } = null!;

    /// <summary>Notification title as displayed on the device.</summary>
    public string Title { get; init; } = null!;

    /// <summary>Notification body text as displayed on the device.</summary>
    public string Message { get; init; } = null!;

    /// <summary>Expo experience ID associated with this notification.</summary>
    public string ExperienceId { get; init; } = null!;

    /// <summary>Expo scope key associated with this notification.</summary>
    public string ScopeKey { get; init; } = null!;

    /// <summary>Structured payload decoded from the notification body JSON.</summary>
    public Body Body { get; init; } = null!;
}

/// <summary>
/// Rust+ encodes the numeric fields below as JSON strings; STJ's number handling reads them
/// from strings (and writes them back as strings) natively, so no custom converters are needed.
/// </summary>
[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
public sealed record Body
{
    /// <summary>The Rust+ server ID.</summary>
    public Guid Id { get; init; }

    /// <summary>IP address or hostname of the Rust+ companion port.</summary>
    public string Ip { get; init; } = null!;

    /// <summary>Rust+ companion port number.</summary>
    public int Port { get; init; }

    /// <summary>Display name of the server.</summary>
    public string Name { get; init; } = null!;

    /// <summary>Optional server description.</summary>
    public string? Desc { get; init; }

    /// <summary>Optional URL to the server's logo image.</summary>
    public string? Logo { get; init; }

    /// <summary>Optional URL to a server banner image.</summary>
    public string? Img { get; init; }

    /// <summary>Optional URL to the server's website.</summary>
    public string? Url { get; init; }

    /// <summary>Steam ID of the player who performed the pairing.</summary>
    public ulong PlayerId { get; init; }

    /// <summary>Rust+ player authentication token for the pairing player.</summary>
    public string PlayerToken { get; init; } = null!;

    /// <summary>Pairing type: <c>"entity"</c> or <c>"server"</c>.</summary>
    public string Type { get; init; } = null!;

    /// <summary>Entity type when <see cref="Type"/> is <c>"entity"</c>: 1 = Smart Switch, 2 = Smart Alarm, 3 = Storage Monitor.</summary>
    public int? EntityType { get; init; }

    /// <summary>Entity ID when <see cref="Type"/> is <c>"entity"</c>.</summary>
    public int? EntityId { get; init; }

    /// <summary>Entity name when <see cref="Type"/> is <c>"entity"</c>.</summary>
    public string? EntityName { get; init; }
}
