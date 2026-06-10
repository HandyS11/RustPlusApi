using ProtoBuf;
using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Data.Events;
using RustPlusApi.Fcm.Registration;
using RustPlusApi.Fcm.Registration.Protobuf;
using RustPlusApi.Fcm.Registration.Steps;
using Xunit;

namespace RustPlusApi.Tests.Unit;

/// <summary>
/// Guards the deterministic, offline-testable parts of the native registration flow:
/// the check-in protobuf contracts, the FID generation, credential persistence and the
/// pairing-notification mapping. The live network flow is only validatable by a real run.
/// </summary>
public class RegistrationTests
{
    [Fact]
    public void AndroidCheckinRequest_RoundTrips()
    {
        var request = new AndroidCheckinRequest
        {
            UserSerialNumber = 0,
            Version = 3,
            Checkin = new AndroidCheckinProto
            {
                Type = AndroidCheckinProto.DeviceType.DeviceChromeBrowser,
                ChromeBuild = new ChromeBuildProto
                {
                    Platform = ChromeBuildProto.PlatformType.Mac,
                    ChromeVersion = "63.0.3234.0",
                    Channel = ChromeBuildProto.ChannelType.Stable
                }
            }
        };

        using var stream = new MemoryStream();
        Serializer.Serialize(stream, request);
        stream.Position = 0;
        var result = Serializer.Deserialize<AndroidCheckinRequest>(stream);

        Assert.Equal(3, result.Version);
        Assert.Equal(AndroidCheckinProto.DeviceType.DeviceChromeBrowser, result.Checkin!.Type);
        Assert.Equal("63.0.3234.0", result.Checkin.ChromeBuild!.ChromeVersion);
        Assert.Equal(ChromeBuildProto.PlatformType.Mac, result.Checkin.ChromeBuild.Platform);
    }

    [Fact]
    public void AndroidCheckinResponse_DecodesFixed64Identity()
    {
        var response = new AndroidCheckinResponse
        {
            StatsOk = true,
            AndroidId = 1234567890123UL,
            SecurityToken = 9876543210987UL
        };

        using var stream = new MemoryStream();
        Serializer.Serialize(stream, response);
        stream.Position = 0;
        var result = Serializer.Deserialize<AndroidCheckinResponse>(stream);

        Assert.Equal(1234567890123UL, result.AndroidId);
        Assert.Equal(9876543210987UL, result.SecurityToken);
    }

    [Fact]
    public void GenerateFirebaseId_Is22CharsUnpaddedWithLeadingNibble()
    {
        for (var i = 0; i < 50; i++)
        {
            var fid = AndroidFcmRegister.GenerateFirebaseId();

            Assert.DoesNotContain('=', fid);
            // 17 bytes -> 24 base64 chars incl. one '=' of padding, stripped -> 23 chars.
            Assert.Equal(23, fid.Length);
            // First byte's high nibble forced to 0b0111 -> top 6 bits are 0b0111xx (28-31) -> 'c'..'f'.
            Assert.Contains(fid[0], "cdef");
        }
    }

    [Fact]
    public void CredentialsStore_RoundTripsFullBlob()
    {
        var credentials = new Credentials
        {
            Gcm = new Gcm { AndroidId = 42, SecurityToken = 7 },
            Fcm = new FcmToken { Token = "fcm-token" },
            ExpoPushToken = "ExponentPushToken[abc]"
        };

        var result = CredentialsStore.Deserialize(CredentialsStore.Serialize(credentials));

        Assert.Equal(42UL, result.Gcm.AndroidId);
        Assert.Equal("fcm-token", result.Fcm!.Token);
        Assert.Equal("ExponentPushToken[abc]", result.ExpoPushToken);
    }

    [Fact]
    public void PairingListener_MapsServerNotificationToRustPlusArgs()
    {
        var notification = new Notification<ServerEvent?>
        {
            PlayerId = 76561198000000000,
            PlayerToken = 123456789,
            Data = new ServerEvent { Ip = "1.2.3.4", Port = 28083, Name = "My Server" }
        };

        var pairing = PairingListener.ToServerPairing(notification);

        Assert.Equal("1.2.3.4", pairing.Ip);
        Assert.Equal(28083, pairing.Port);
        Assert.Equal(76561198000000000UL, pairing.PlayerId);
        Assert.Equal(123456789, pairing.PlayerToken);
        Assert.Equal("My Server", pairing.Name);
    }

    /// <summary>
    /// Covers the <c>server == null</c> arm of <see cref="PairingListener.ToServerPairing"/>:
    /// all <c>server?.Property ?? fallback</c> null-conditional false-branches.
    /// </summary>
    [Fact]
    public void PairingListener_NullServerData_FallsBackToDefaults()
    {
        var notification = new Notification<ServerEvent?>
        {
            PlayerId = 76561198000000000,
            PlayerToken = 999,
            Data = null  // server is null → all server?.Xxx produce null → ?? fallbacks used
        };

        var pairing = PairingListener.ToServerPairing(notification);

        Assert.Equal(string.Empty, pairing.Ip);
        Assert.Equal(0, pairing.Port);
        Assert.Equal(76561198000000000UL, pairing.PlayerId);
        Assert.Equal(999, pairing.PlayerToken);
        Assert.Null(pairing.Name);
    }
}
