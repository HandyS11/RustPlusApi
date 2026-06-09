using ProtoBuf;

// Code-first contracts for the MCS (Mobile Connection Server) protocol used by the FCM
// listener. Hand-authored from the frozen Chromium `mcs.proto` (proto2). protobuf-net
// serializes by field number, so the [ProtoMember] numbers below are the wire contract.
// Optional scalars are modelled as nullable, required fields carry IsRequired = true.
#pragma warning disable IDE0130 // Namespace does not match folder structure — McsProto matches the wire protocol name
namespace McsProto;
#pragma warning restore IDE0130

/// <summary>TAG: 0 — Heartbeat ping sent by the server to check connection liveness.</summary>
[ProtoContract]
public sealed class HeartbeatPing
{
    /// <summary>Monotonically increasing stream counter.</summary>
    [ProtoMember(1)] public int? StreamId { get; set; }

    /// <summary>Last stream ID acknowledged by the sender.</summary>
    [ProtoMember(2)] public int? LastStreamIdReceived { get; set; }

    /// <summary>Connection status flags.</summary>
    [ProtoMember(3)] public long? Status { get; set; }
}

/// <summary>TAG: 1 — Heartbeat acknowledgment sent in response to a <see cref="HeartbeatPing"/>.</summary>
[ProtoContract]
public sealed class HeartbeatAck
{
    /// <summary>Monotonically increasing stream counter.</summary>
    [ProtoMember(1)] public int? StreamId { get; set; }

    /// <summary>Last stream ID acknowledged by the sender.</summary>
    [ProtoMember(2)] public int? LastStreamIdReceived { get; set; }

    /// <summary>Connection status flags.</summary>
    [ProtoMember(3)] public long? Status { get; set; }
}

/// <summary>Error detail attached to a <see cref="LoginResponse"/> or <see cref="IqStanza"/>.</summary>
[ProtoContract]
public sealed class ErrorInfo
{
    /// <summary>Numeric error code.</summary>
    [ProtoMember(1, IsRequired = true)] public int Code { get; set; }

    /// <summary>Human-readable error description.</summary>
    [ProtoMember(2)] public string? Message { get; set; }

    /// <summary>Error type string.</summary>
    [ProtoMember(3)] public string? Type { get; set; }

    /// <summary>Optional protocol extension payload attached to the error.</summary>
    [ProtoMember(4)] public Extension? Extension { get; set; }
}

/// <summary>A key/value configuration setting exchanged during MCS login.</summary>
[ProtoContract]
public sealed class Setting
{
    /// <summary>Setting name.</summary>
    [ProtoMember(1, IsRequired = true)] public string Name { get; set; } = null!;

    /// <summary>Setting value.</summary>
    [ProtoMember(2, IsRequired = true)] public string Value { get; set; } = null!;
}

/// <summary>Heartbeat statistics uploaded to the server for diagnostics.</summary>
[ProtoContract]
public sealed class HeartbeatStat
{
    /// <summary>IP address of the MCS endpoint.</summary>
    [ProtoMember(1, IsRequired = true)] public string Ip { get; set; } = null!;

    /// <summary><see langword="true"/> if the heartbeat timed out.</summary>
    [ProtoMember(2, IsRequired = true)] public bool Timeout { get; set; }

    /// <summary>Configured heartbeat interval, in milliseconds.</summary>
    [ProtoMember(3, IsRequired = true)] public int IntervalMs { get; set; }
}

/// <summary>Server-driven heartbeat configuration returned in <see cref="LoginResponse"/>.</summary>
[ProtoContract]
public sealed class HeartbeatConfig
{
    /// <summary>Whether the client should upload <see cref="HeartbeatStat"/> data.</summary>
    [ProtoMember(1)] public bool? UploadStat { get; set; }

    /// <summary>IP address to use for heartbeat probes.</summary>
    [ProtoMember(2)] public string? Ip { get; set; }

    /// <summary>Heartbeat interval the server requests, in milliseconds.</summary>
    [ProtoMember(3)] public int? IntervalMs { get; set; }
}

/// <summary>Client-reported connectivity event, bundled into <see cref="LoginRequest.ClientEvents"/>.</summary>
[ProtoContract]
public sealed class ClientEvent
{
    /// <summary>Discriminator for the type of client event.</summary>
    public enum Type
    {
        /// <summary>Unknown / unset event.</summary>
        Unknown = 0,
        /// <summary>Some events were discarded due to capacity.</summary>
        DiscardedEvents = 1,
        /// <summary>A connection attempt failed.</summary>
        FailedConnection = 2,
        /// <summary>A connection attempt succeeded.</summary>
        SuccessfulConnection = 3,
    }

    /// <summary>Lowercase to avoid clashing with the nested <see cref="Type"/> enum.</summary>
    [ProtoMember(1)] public Type? type { get; set; }

    /// <summary>Number of events discarded (set for <see cref="Type.DiscardedEvents"/>).</summary>
    [ProtoMember(100)] public uint? NumberDiscardedEvents { get; set; }

    /// <summary>Network type code (e.g. 1 = WiFi).</summary>
    [ProtoMember(200)] public int? NetworkType { get; set; }

    /// <summary>UTC epoch milliseconds when the connection attempt started.</summary>
    [ProtoMember(202)] public ulong? TimeConnectionStartedMs { get; set; }

    /// <summary>UTC epoch milliseconds when the connection attempt ended.</summary>
    [ProtoMember(203)] public ulong? TimeConnectionEndedMs { get; set; }

    /// <summary>OS-level error code when the connection failed.</summary>
    [ProtoMember(204)] public int? ErrorCode { get; set; }

    /// <summary>UTC epoch milliseconds when the connection was fully established.</summary>
    [ProtoMember(300)] public ulong? TimeConnectionEstablishedMs { get; set; }
}

/// <summary>TAG: 2 — MCS login request sent immediately after the TLS handshake.</summary>
[ProtoContract]
public sealed class LoginRequest
{
    /// <summary>Authentication service used to verify the GCM identity.</summary>
    public enum AuthService
    {
        /// <summary>Authenticate using an Android ID / security-token pair.</summary>
        AndroidId = 2,
    }

    /// <summary>Client ID string (e.g. <c>chrome-63.0.3234.0</c>).</summary>
    [ProtoMember(1, IsRequired = true)] public string Id { get; set; } = null!;

    /// <summary>Domain for the XMPP JID (always <c>mcs.android.com</c>).</summary>
    [ProtoMember(2, IsRequired = true)] public string Domain { get; set; } = null!;

    /// <summary>GCM Android ID, used as the XMPP user part.</summary>
    [ProtoMember(3, IsRequired = true)] public string User { get; set; } = null!;

    /// <summary>GCM Android ID, used as the XMPP resource part.</summary>
    [ProtoMember(4, IsRequired = true)] public string Resource { get; set; } = null!;

    /// <summary>GCM security token used to authenticate.</summary>
    [ProtoMember(5, IsRequired = true)] public string AuthToken { get; set; } = null!;

    /// <summary>Android device ID string (hex-encoded Android ID prefixed with <c>android-</c>).</summary>
    [ProtoMember(6)] public string? DeviceId { get; set; }

    /// <summary>Last RMQ (reliable message queue) ID seen by the client, for resumption.</summary>
    [ProtoMember(7)] public long? LastRmqId { get; set; }

    /// <summary>Client configuration settings.</summary>
    [ProtoMember(8)] public List<Setting> Settings { get; } = [];

    /// <summary>Persistent IDs the client has already processed and wants the server to skip.</summary>
    [ProtoMember(10)] public List<string> ReceivedPersistentIds { get; } = [];

    /// <summary>Whether the client supports adaptive heartbeat intervals.</summary>
    [ProtoMember(12)] public bool? AdaptiveHeartbeat { get; set; }

    /// <summary>Heartbeat statistics from the previous session, if any.</summary>
    [ProtoMember(13)] public HeartbeatStat? HeartbeatStat { get; set; }

    /// <summary>Whether the client supports RMQ2 (reliable message queue v2).</summary>
    [ProtoMember(14)] public bool? UseRmq2 { get; set; }

    /// <summary>Google account ID, if authenticated.</summary>
    [ProtoMember(15)] public long? AccountId { get; set; }

    /// <summary>Lowercase to avoid clashing with the nested <see cref="AuthService"/> enum.</summary>
    [ProtoMember(16)] public AuthService? auth_service { get; set; }

    /// <summary>Network type code at the time of login (e.g. 1 = WiFi).</summary>
    [ProtoMember(17)] public int? NetworkType { get; set; }

    /// <summary>Connection status flags.</summary>
    [ProtoMember(18)] public long? Status { get; set; }

    /// <summary>Client connectivity events from the previous session.</summary>
    [ProtoMember(22)] public List<ClientEvent> ClientEvents { get; } = [];
}

/// <summary>TAG: 3 — Server response to a <see cref="LoginRequest"/>.</summary>
[ProtoContract]
public sealed class LoginResponse
{
    /// <summary>The XMPP JID assigned to this session by the server.</summary>
    [ProtoMember(1, IsRequired = true)] public string Id { get; set; } = null!;

    /// <summary>Full JID including resource, if assigned.</summary>
    [ProtoMember(2)] public string? Jid { get; set; }

    /// <summary>Error detail if the login was rejected.</summary>
    [ProtoMember(3)] public ErrorInfo? Error { get; set; }

    /// <summary>Server configuration settings for the session.</summary>
    [ProtoMember(4)] public List<Setting> Settings { get; } = [];

    /// <summary>Initial stream ID assigned by the server.</summary>
    [ProtoMember(5)] public int? StreamId { get; set; }

    /// <summary>Last stream ID the server has received from the client.</summary>
    [ProtoMember(6)] public int? LastStreamIdReceived { get; set; }

    /// <summary>Server-requested heartbeat configuration.</summary>
    [ProtoMember(7)] public HeartbeatConfig? HeartbeatConfig { get; set; }

    /// <summary>Server UTC timestamp at the moment the response was generated, in milliseconds.</summary>
    [ProtoMember(8)] public long? ServerTimestamp { get; set; }
}

/// <summary>XMPP stream-level error sent before the server closes the connection.</summary>
[ProtoContract]
public sealed class StreamErrorStanza
{
    /// <summary>XMPP stream error condition name (e.g. <c>conflict</c>).</summary>
    [ProtoMember(1, IsRequired = true)] public string Type { get; set; } = null!;

    /// <summary>Optional human-readable description of the error.</summary>
    [ProtoMember(2)] public string? Text { get; set; }
}

/// <summary>TAG: 4 — Signals that the server is closing the connection.</summary>
[ProtoContract]
public sealed class Close;

/// <summary>An opaque binary extension payload carried inside other MCS messages.</summary>
[ProtoContract]
public sealed class Extension
{
    /// <summary>Extension type identifier.</summary>
    [ProtoMember(1, IsRequired = true)] public int Id { get; set; }

    /// <summary>Raw binary payload of the extension.</summary>
    [ProtoMember(2, IsRequired = true)] public byte[] Data { get; set; } = null!;
}

/// <summary>TAG: 7 — XMPP IQ (info/query) stanza used for server-initiated operations.</summary>
[ProtoContract]
public sealed class IqStanza
{
    /// <summary>IQ stanza type.</summary>
    public enum IqType
    {
        /// <summary>Request information.</summary>
        Get = 0,
        /// <summary>Set or update a value.</summary>
        Set = 1,
        /// <summary>Successful response to a Get or Set.</summary>
        Result = 2,
        /// <summary>Error response.</summary>
        IqError = 3,
    }

    /// <summary>RMQ message ID for reliable delivery tracking.</summary>
    [ProtoMember(1)] public long? RmqId { get; set; }

    /// <summary>IQ stanza type.</summary>
    [ProtoMember(2, IsRequired = true)] public IqType Type { get; set; }

    /// <summary>Stanza ID for matching requests and responses.</summary>
    [ProtoMember(3, IsRequired = true)] public string Id { get; set; } = null!;

    /// <summary>Sender JID.</summary>
    [ProtoMember(4)] public string? From { get; set; }

    /// <summary>Recipient JID.</summary>
    [ProtoMember(5)] public string? To { get; set; }

    /// <summary>Error detail if this is an error response.</summary>
    [ProtoMember(6)] public ErrorInfo? Error { get; set; }

    /// <summary>Protocol extension payload.</summary>
    [ProtoMember(7)] public Extension? Extension { get; set; }

    /// <summary>Persistent message ID for deduplication on reconnect.</summary>
    [ProtoMember(8)] public string? PersistentId { get; set; }

    /// <summary>Stream ID of this message.</summary>
    [ProtoMember(9)] public int? StreamId { get; set; }

    /// <summary>Last stream ID received by the sender.</summary>
    [ProtoMember(10)] public int? LastStreamIdReceived { get; set; }

    /// <summary>Google account ID associated with this stanza.</summary>
    [ProtoMember(11)] public long? AccountId { get; set; }

    /// <summary>Status flags.</summary>
    [ProtoMember(12)] public long? Status { get; set; }
}

/// <summary>A key/value pair of application-specific data carried inside a <see cref="DataMessageStanza"/>.</summary>
[ProtoContract]
public sealed class AppData
{
    /// <summary>Data key.</summary>
    [ProtoMember(1, IsRequired = true)] public string Key { get; set; } = null!;

    /// <summary>Data value.</summary>
    [ProtoMember(2, IsRequired = true)] public string Value { get; set; } = null!;
}

/// <summary>TAG: 8 — A push notification data message delivered from the FCM upstream.</summary>
[ProtoContract]
public sealed class DataMessageStanza
{
    /// <summary>Application-level message ID.</summary>
    [ProtoMember(2)] public string? Id { get; set; }

    /// <summary>Sender address (GCM sender ID or project number).</summary>
    [ProtoMember(3, IsRequired = true)] public string From { get; set; } = null!;

    /// <summary>Recipient address.</summary>
    [ProtoMember(4)] public string? To { get; set; }

    /// <summary>Application package name / category that identifies the target app.</summary>
    [ProtoMember(5, IsRequired = true)] public string Category { get; set; } = null!;

    /// <summary>FCM registration token of the recipient device.</summary>
    [ProtoMember(6)] public string? Token { get; set; }

    /// <summary>Application-defined key/value pairs carrying the notification payload.</summary>
    [ProtoMember(7)] public List<AppData> AppDatas { get; } = [];

    /// <summary><see langword="true"/> if the message was delivered from a trusted server.</summary>
    [ProtoMember(8)] public bool? FromTrustedServer { get; set; }

    /// <summary>Persistent message ID used for deduplication on reconnect.</summary>
    [ProtoMember(9)] public string? PersistentId { get; set; }

    /// <summary>Stream ID of this message.</summary>
    [ProtoMember(10)] public int? StreamId { get; set; }

    /// <summary>Last stream ID received by the sender.</summary>
    [ProtoMember(11)] public int? LastStreamIdReceived { get; set; }

    /// <summary>FCM registration ID of the device.</summary>
    [ProtoMember(13)] public string? RegId { get; set; }

    /// <summary>Google account user ID associated with this message.</summary>
    [ProtoMember(16)] public long? DeviceUserId { get; set; }

    /// <summary>Time-to-live for the message, in seconds.</summary>
    [ProtoMember(17)] public int? Ttl { get; set; }

    /// <summary>UTC epoch milliseconds when the message was enqueued on the FCM server.</summary>
    [ProtoMember(18)] public long? Sent { get; set; }

    /// <summary>Number of messages queued ahead of this one at the time of delivery.</summary>
    [ProtoMember(19)] public int? Queued { get; set; }

    /// <summary>Status flags.</summary>
    [ProtoMember(20)] public long? Status { get; set; }

    /// <summary>Raw binary payload, if present.</summary>
    [ProtoMember(21)] public byte[]? RawData { get; set; }

    /// <summary><see langword="true"/> if the client should ack this message immediately without batching.</summary>
    [ProtoMember(24)] public bool? ImmediateAck { get; set; }
}

/// <summary>Acknowledges receipt of all messages up to the current stream ID.</summary>
[ProtoContract]
public sealed class StreamAck;

/// <summary>Acknowledges a specific set of messages by persistent ID.</summary>
[ProtoContract]
public sealed class SelectiveAck
{
    /// <summary>Persistent IDs of the messages being acknowledged.</summary>
    [ProtoMember(1)] public List<string> Ids { get; } = [];
}
