using ProtoBuf;

// Code-first contracts for the Android GCM check-in (checkin.proto / android_checkin.proto).
// Field numbers are the wire contract; only the subset push-receiver populates is modelled.
namespace RustPlusApi.Fcm.Registration.Protobuf;

[ProtoContract]
public sealed class ChromeBuildProto
{
    public enum PlatformType { Win = 1, Mac = 2, Linux = 3, Cros = 4, Ios = 5, Android = 6 }

    public enum ChannelType { Stable = 1, Beta = 2, Dev = 3, Canary = 4, Unknown = 5 }

    [ProtoMember(1)] public PlatformType? Platform { get; set; }
    [ProtoMember(2)] public string? ChromeVersion { get; set; }
    [ProtoMember(3)] public ChannelType? Channel { get; set; }
}

[ProtoContract]
public sealed class AndroidCheckinProto
{
    public enum DeviceType { DeviceAndroidOs = 1, DeviceIosOs = 2, DeviceChromeBrowser = 3, DeviceChromeOs = 4 }

    [ProtoMember(2)] public long? LastCheckinMsec { get; set; }
    [ProtoMember(12)] public DeviceType? Type { get; set; }
    [ProtoMember(13)] public ChromeBuildProto? ChromeBuild { get; set; }
}

[ProtoContract]
public sealed class AndroidCheckinRequest
{
    [ProtoMember(2)] public long? Id { get; set; }
    [ProtoMember(4)] public AndroidCheckinProto? Checkin { get; set; }
    [ProtoMember(13, DataFormat = DataFormat.FixedSize)] public ulong? SecurityToken { get; set; }
    [ProtoMember(14)] public int? Version { get; set; }
    [ProtoMember(22)] public int? UserSerialNumber { get; set; }
}

[ProtoContract]
public sealed class AndroidCheckinResponse
{
    [ProtoMember(1)] public bool? StatsOk { get; set; }
    [ProtoMember(7, DataFormat = DataFormat.FixedSize)] public ulong? AndroidId { get; set; }
    [ProtoMember(8, DataFormat = DataFormat.FixedSize)] public ulong? SecurityToken { get; set; }
}
