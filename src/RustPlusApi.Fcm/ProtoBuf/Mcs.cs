using ProtoBuf;

// Code-first contracts for the MCS (Mobile Connection Server) protocol used by the FCM
// listener. Hand-authored from the frozen Chromium `mcs.proto` (proto2). protobuf-net
// serializes by field number, so the [ProtoMember] numbers below are the wire contract;
// optional scalars are modelled as nullable, required fields carry IsRequired = true.
namespace McsProto;

/// <summary>TAG: 0</summary>
[ProtoContract]
public sealed class HeartbeatPing
{
    [ProtoMember(1)] public int? StreamId { get; set; }
    [ProtoMember(2)] public int? LastStreamIdReceived { get; set; }
    [ProtoMember(3)] public long? Status { get; set; }
}

/// <summary>TAG: 1</summary>
[ProtoContract]
public sealed class HeartbeatAck
{
    [ProtoMember(1)] public int? StreamId { get; set; }
    [ProtoMember(2)] public int? LastStreamIdReceived { get; set; }
    [ProtoMember(3)] public long? Status { get; set; }
}

[ProtoContract]
public sealed class ErrorInfo
{
    [ProtoMember(1, IsRequired = true)] public int Code { get; set; }
    [ProtoMember(2)] public string? Message { get; set; }
    [ProtoMember(3)] public string? Type { get; set; }
    [ProtoMember(4)] public Extension? Extension { get; set; }
}

[ProtoContract]
public sealed class Setting
{
    [ProtoMember(1, IsRequired = true)] public string Name { get; set; } = null!;
    [ProtoMember(2, IsRequired = true)] public string Value { get; set; } = null!;
}

[ProtoContract]
public sealed class HeartbeatStat
{
    [ProtoMember(1, IsRequired = true)] public string Ip { get; set; } = null!;
    [ProtoMember(2, IsRequired = true)] public bool Timeout { get; set; }
    [ProtoMember(3, IsRequired = true)] public int IntervalMs { get; set; }
}

[ProtoContract]
public sealed class HeartbeatConfig
{
    [ProtoMember(1)] public bool? UploadStat { get; set; }
    [ProtoMember(2)] public string? Ip { get; set; }
    [ProtoMember(3)] public int? IntervalMs { get; set; }
}

[ProtoContract]
public sealed class ClientEvent
{
    public enum Type
    {
        Unknown = 0,
        DiscardedEvents = 1,
        FailedConnection = 2,
        SuccessfulConnection = 3,
    }

    // Lowercase to avoid clashing with the nested Type enum.
    [ProtoMember(1)] public Type? type { get; set; }
    [ProtoMember(100)] public uint? NumberDiscardedEvents { get; set; }
    [ProtoMember(200)] public int? NetworkType { get; set; }
    [ProtoMember(202)] public ulong? TimeConnectionStartedMs { get; set; }
    [ProtoMember(203)] public ulong? TimeConnectionEndedMs { get; set; }
    [ProtoMember(204)] public int? ErrorCode { get; set; }
    [ProtoMember(300)] public ulong? TimeConnectionEstablishedMs { get; set; }
}

/// <summary>TAG: 2</summary>
[ProtoContract]
public sealed class LoginRequest
{
    public enum AuthService
    {
        AndroidId = 2,
    }

    [ProtoMember(1, IsRequired = true)] public string Id { get; set; } = null!;
    [ProtoMember(2, IsRequired = true)] public string Domain { get; set; } = null!;
    [ProtoMember(3, IsRequired = true)] public string User { get; set; } = null!;
    [ProtoMember(4, IsRequired = true)] public string Resource { get; set; } = null!;
    [ProtoMember(5, IsRequired = true)] public string AuthToken { get; set; } = null!;
    [ProtoMember(6)] public string? DeviceId { get; set; }
    [ProtoMember(7)] public long? LastRmqId { get; set; }
    [ProtoMember(8)] public List<Setting> Settings { get; } = [];
    [ProtoMember(10)] public List<string> ReceivedPersistentIds { get; } = [];
    [ProtoMember(12)] public bool? AdaptiveHeartbeat { get; set; }
    [ProtoMember(13)] public HeartbeatStat? HeartbeatStat { get; set; }
    [ProtoMember(14)] public bool? UseRmq2 { get; set; }
    [ProtoMember(15)] public long? AccountId { get; set; }
    // Lowercase to avoid clashing with the nested AuthService enum.
    [ProtoMember(16)] public AuthService? auth_service { get; set; }
    [ProtoMember(17)] public int? NetworkType { get; set; }
    [ProtoMember(18)] public long? Status { get; set; }
    [ProtoMember(22)] public List<ClientEvent> ClientEvents { get; } = [];
}

/// <summary>TAG: 3</summary>
[ProtoContract]
public sealed class LoginResponse
{
    [ProtoMember(1, IsRequired = true)] public string Id { get; set; } = null!;
    [ProtoMember(2)] public string? Jid { get; set; }
    [ProtoMember(3)] public ErrorInfo? Error { get; set; }
    [ProtoMember(4)] public List<Setting> Settings { get; } = [];
    [ProtoMember(5)] public int? StreamId { get; set; }
    [ProtoMember(6)] public int? LastStreamIdReceived { get; set; }
    [ProtoMember(7)] public HeartbeatConfig? HeartbeatConfig { get; set; }
    [ProtoMember(8)] public long? ServerTimestamp { get; set; }
}

[ProtoContract]
public sealed class StreamErrorStanza
{
    [ProtoMember(1, IsRequired = true)] public string Type { get; set; } = null!;
    [ProtoMember(2)] public string? Text { get; set; }
}

/// <summary>TAG: 4</summary>
[ProtoContract]
public sealed class Close
{
}

[ProtoContract]
public sealed class Extension
{
    [ProtoMember(1, IsRequired = true)] public int Id { get; set; }
    [ProtoMember(2, IsRequired = true)] public byte[] Data { get; set; } = null!;
}

/// <summary>TAG: 7</summary>
[ProtoContract]
public sealed class IqStanza
{
    public enum IqType
    {
        Get = 0,
        Set = 1,
        Result = 2,
        IqError = 3,
    }

    [ProtoMember(1)] public long? RmqId { get; set; }
    [ProtoMember(2, IsRequired = true)] public IqType Type { get; set; }
    [ProtoMember(3, IsRequired = true)] public string Id { get; set; } = null!;
    [ProtoMember(4)] public string? From { get; set; }
    [ProtoMember(5)] public string? To { get; set; }
    [ProtoMember(6)] public ErrorInfo? Error { get; set; }
    [ProtoMember(7)] public Extension? Extension { get; set; }
    [ProtoMember(8)] public string? PersistentId { get; set; }
    [ProtoMember(9)] public int? StreamId { get; set; }
    [ProtoMember(10)] public int? LastStreamIdReceived { get; set; }
    [ProtoMember(11)] public long? AccountId { get; set; }
    [ProtoMember(12)] public long? Status { get; set; }
}

[ProtoContract]
public sealed class AppData
{
    [ProtoMember(1, IsRequired = true)] public string Key { get; set; } = null!;
    [ProtoMember(2, IsRequired = true)] public string Value { get; set; } = null!;
}

/// <summary>TAG: 8</summary>
[ProtoContract]
public sealed class DataMessageStanza
{
    [ProtoMember(2)] public string? Id { get; set; }
    [ProtoMember(3, IsRequired = true)] public string From { get; set; } = null!;
    [ProtoMember(4)] public string? To { get; set; }
    [ProtoMember(5, IsRequired = true)] public string Category { get; set; } = null!;
    [ProtoMember(6)] public string? Token { get; set; }
    [ProtoMember(7)] public List<AppData> AppDatas { get; } = [];
    [ProtoMember(8)] public bool? FromTrustedServer { get; set; }
    [ProtoMember(9)] public string? PersistentId { get; set; }
    [ProtoMember(10)] public int? StreamId { get; set; }
    [ProtoMember(11)] public int? LastStreamIdReceived { get; set; }
    [ProtoMember(13)] public string? RegId { get; set; }
    [ProtoMember(16)] public long? DeviceUserId { get; set; }
    [ProtoMember(17)] public int? Ttl { get; set; }
    [ProtoMember(18)] public long? Sent { get; set; }
    [ProtoMember(19)] public int? Queued { get; set; }
    [ProtoMember(20)] public long? Status { get; set; }
    [ProtoMember(21)] public byte[]? RawData { get; set; }
    [ProtoMember(24)] public bool? ImmediateAck { get; set; }
}

[ProtoContract]
public sealed class StreamAck
{
}

[ProtoContract]
public sealed class SelectiveAck
{
    [ProtoMember(1)] public List<string> Ids { get; } = [];
}
