using ProtoBuf;

// Code-first contracts for the Android GCM check-in (checkin.proto / android_checkin.proto).
// Field numbers are the wire contract; only the subset push-receiver populates is modelled.
namespace RustPlusApi.Fcm.Registration.Protobuf;

/// <summary>Chrome build descriptor sent in the GCM check-in request.</summary>
[ProtoContract]
public sealed class ChromeBuildProto
{
    /// <summary>Chrome release channel.</summary>
    public enum ChannelType
    {
        /// <summary>Stable channel.</summary>
        Stable = 1,

        /// <summary>Beta channel.</summary>
        Beta = 2,

        /// <summary>Dev channel.</summary>
        Dev = 3,

        /// <summary>Canary channel.</summary>
        Canary = 4,

        /// <summary>Unknown channel.</summary>
        Unknown = 5
    }

    /// <summary>Host platform of the Chrome client.</summary>
    public enum PlatformType
    {
        /// <summary>Windows.</summary>
        Win = 1,

        /// <summary>macOS.</summary>
        Mac = 2,

        /// <summary>Linux.</summary>
        Linux = 3,

        /// <summary>Chrome OS.</summary>
        Cros = 4,

        /// <summary>iOS.</summary>
        Ios = 5,

        /// <summary>Android.</summary>
        Android = 6
    }

    /// <summary>Host platform.</summary>
    [ProtoMember(1)]
    public PlatformType? Platform { get; set; }

    /// <summary>Chrome version string (e.g. <c>63.0.3234.0</c>).</summary>
    [ProtoMember(2)]
    public string? ChromeVersion { get; set; }

    /// <summary>Chrome release channel.</summary>
    [ProtoMember(3)]
    public ChannelType? Channel { get; set; }
}

/// <summary>Android check-in descriptor embedded in the check-in request.</summary>
[ProtoContract]
public sealed class AndroidCheckinProto
{
    /// <summary>Device type reported to GCM.</summary>
    public enum DeviceType
    {
        /// <summary>Android OS device.</summary>
        DeviceAndroidOs = 1,

        /// <summary>iOS device.</summary>
        DeviceIosOs = 2,

        /// <summary>Chrome browser (used by push-receiver).</summary>
        DeviceChromeBrowser = 3,

        /// <summary>Chrome OS device.</summary>
        DeviceChromeOs = 4
    }

    /// <summary>Timestamp of the last successful check-in, in milliseconds since epoch.</summary>
    [ProtoMember(2)]
    public long? LastCheckinMsec { get; set; }

    /// <summary>Device type reported to GCM.</summary>
    [ProtoMember(12)]
    public DeviceType? Type { get; set; }

    /// <summary>Chrome build details included when <see cref="Type"/> is <see cref="DeviceType.DeviceChromeBrowser"/>.</summary>
    [ProtoMember(13)]
    public ChromeBuildProto? ChromeBuild { get; set; }
}

/// <summary>Protobuf request body for the GCM check-in endpoint.</summary>
[ProtoContract]
public sealed class AndroidCheckinRequest
{
    /// <summary>Existing Android ID, or zero for the first check-in.</summary>
    [ProtoMember(2)]
    public long? Id { get; set; }

    /// <summary>Android check-in descriptor.</summary>
    [ProtoMember(4)]
    public AndroidCheckinProto? Checkin { get; set; }

    /// <summary>Existing security token, or zero for the first check-in.</summary>
    [ProtoMember(13, DataFormat = DataFormat.FixedSize)]
    public ulong? SecurityToken { get; set; }

    /// <summary>Check-in protocol version (3 for the Chrome-browser flow).</summary>
    [ProtoMember(14)]
    public int? Version { get; set; }

    /// <summary>User serial number (0 for the default user).</summary>
    [ProtoMember(22)]
    public int? UserSerialNumber { get; set; }
}

/// <summary>Protobuf response body from the GCM check-in endpoint.</summary>
[ProtoContract]
public sealed class AndroidCheckinResponse
{
    /// <summary><see langword="true"/> when the check-in was accepted by the server.</summary>
    [ProtoMember(1)]
    public bool? StatsOk { get; set; }

    /// <summary>The assigned Android ID (GCM device identity).</summary>
    [ProtoMember(7, DataFormat = DataFormat.FixedSize)]
    public ulong? AndroidId { get; set; }

    /// <summary>The security token paired with <see cref="AndroidId"/>.</summary>
    [ProtoMember(8, DataFormat = DataFormat.FixedSize)]
    public ulong? SecurityToken { get; set; }
}
