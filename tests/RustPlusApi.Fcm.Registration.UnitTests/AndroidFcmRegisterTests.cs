using ProtoBuf;
using RustPlusApi.Fcm.Registration;
using RustPlusApi.Fcm.Registration.Protobuf;
using RustPlusApi.Fcm.Registration.Steps;
using RustPlusApi.Fcm.Registration.UnitTests.TestHelpers;
using System.Net;
using System.Text;
using Xunit;

namespace RustPlusApi.Fcm.Registration.UnitTests;

/// <summary>Offline coverage of the GCM check-in / FIS / FCM-register HTTP steps via a stub handler.</summary>
public class AndroidFcmRegisterTests
{
    private static byte[] CheckinResponseBytes(ulong androidId, ulong securityToken)
    {
        using var ms = new MemoryStream();
        Serializer.Serialize(ms, new AndroidCheckinResponse
        {
            StatsOk = true, AndroidId = androidId, SecurityToken = securityToken
        });
        return ms.ToArray();
    }

    /// <summary>Parses an x-www-form-urlencoded body into a key/value dictionary.</summary>
    /// <param name="body">The raw form-encoded request body bytes.</param>
    private static Dictionary<string, string> ParseForm(byte[] body)
    {
        // FormUrlEncodedContent escapes spaces as '+', so decode that before percent-unescaping.
        static string Decode(string s) => Uri.UnescapeDataString(s.Replace('+', ' '));

        var text = Encoding.UTF8.GetString(body);
        return text.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(parts => Decode(parts[0]),
                parts => parts.Length > 1 ? Decode(parts[1]) : string.Empty);
    }

    /// <summary>Asserts the request carries exactly one value for <paramref name="name"/> and returns it.</summary>
    /// <param name="request">The recorded request to read the header from.</param>
    /// <param name="name">The header name to look up.</param>
    private static string SingleHeader(HttpRequestMessage request, string name)
    {
        Assert.True(request.Headers.TryGetValues(name, out var values), $"Missing header {name}");
        return Assert.Single(values!);
    }

    [Fact]
    public async Task CheckInAsync_DeserializesIdentity()
    {
        var handler = StubHttpMessageHandler.Always(HttpStatusCode.OK, CheckinResponseBytes(111, 222));
        var register = new AndroidFcmRegister(handler.CreateClient());

        var gcm = await register.CheckInAsync();

        Assert.Equal(111UL, gcm.AndroidId);
        Assert.Equal(222UL, gcm.SecurityToken);
        Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal(RegistrationConstants.CheckinUrl, handler.Requests[0].RequestUri!.ToString());
        // The check-in body is a protobuf-serialized AndroidCheckinRequest; round-trip it back
        // and assert the exact field values the step set.
        var sent = Serializer.Deserialize<AndroidCheckinRequest>(new MemoryStream(handler.RequestBodies[0]));
        Assert.Equal(0, sent.UserSerialNumber!.Value);
        Assert.Equal(3, sent.Version!.Value);
        Assert.Equal(AndroidCheckinProto.DeviceType.DeviceChromeBrowser, sent.Checkin!.Type!.Value);
        Assert.Equal(ChromeBuildProto.PlatformType.Mac, sent.Checkin.ChromeBuild!.Platform!.Value);
        Assert.Equal(RegistrationConstants.ChromeVersion, sent.Checkin.ChromeBuild.ChromeVersion);
        Assert.Equal(ChromeBuildProto.ChannelType.Stable, sent.Checkin.ChromeBuild.Channel!.Value);
    }

    /// <summary>
    /// A periodic check-in for an already-registered device (the reference push-receiver does one
    /// before every MCS connect) must carry the existing identity so Google refreshes that device's
    /// registry entry instead of minting a new one.
    /// </summary>
    [Fact]
    public async Task CheckInAsync_WithExistingIdentity_SendsIdAndSecurityToken()
    {
        var handler = StubHttpMessageHandler.Always(HttpStatusCode.OK, CheckinResponseBytes(111, 222));
        var register = new AndroidFcmRegister(handler.CreateClient());

        var gcm = await register.CheckInAsync(new RustPlusApi.Fcm.Data.Gcm
        {
            AndroidId = 111, SecurityToken = 222
        });

        Assert.Equal(111UL, gcm.AndroidId);
        Assert.Equal(222UL, gcm.SecurityToken);
        var sent = Serializer.Deserialize<AndroidCheckinRequest>(new MemoryStream(handler.RequestBodies[0]));
        Assert.Equal(111L, sent.Id);
        Assert.Equal(222UL, sent.SecurityToken);
    }

    /// <summary>A first check-in (no identity) must NOT claim an existing Android id / security token.</summary>
    [Fact]
    public async Task CheckInAsync_WithoutIdentity_OmitsIdAndSecurityToken()
    {
        var handler = StubHttpMessageHandler.Always(HttpStatusCode.OK, CheckinResponseBytes(111, 222));
        var register = new AndroidFcmRegister(handler.CreateClient());

        await register.CheckInAsync();

        var sent = Serializer.Deserialize<AndroidCheckinRequest>(new MemoryStream(handler.RequestBodies[0]));
        Assert.Null(sent.Id);
        Assert.Null(sent.SecurityToken);
    }

    [Fact]
    public async Task InstallAsync_ReturnsAuthToken()
    {
        var handler = StubHttpMessageHandler.Always(HttpStatusCode.OK,
            """{ "authToken": { "token": "fis-token" } }""");
        var register = new AndroidFcmRegister(handler.CreateClient());

        var token = await register.InstallAsync();

        Assert.Equal("fis-token", token);

        var request = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(RegistrationConstants.FirebaseInstallationsUrl, request.RequestUri!.ToString());

        // Exact header NAMES and VALUES set by the FIS step.
        Assert.Equal(RegistrationConstants.ApiKey, SingleHeader(request, "x-goog-api-key"));
        Assert.Equal(RegistrationConstants.AndroidPackageName, SingleHeader(request, "X-Android-Package"));
        Assert.Equal(RegistrationConstants.AndroidPackageCert, SingleHeader(request, "X-Android-Cert"));
        Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);

        // Exact request BODY fields.
        using var doc = System.Text.Json.JsonDocument.Parse(Encoding.UTF8.GetString(handler.RequestBodies[0]));
        var root = doc.RootElement;
        Assert.Equal(RegistrationConstants.GmsAppId, root.GetProperty("appId").GetString());
        Assert.Equal("FIS_v2", root.GetProperty("authVersion").GetString());
        Assert.Equal("a:17.0.0", root.GetProperty("sdkVersion").GetString());
        // fid is random per-run, but must be a non-empty base64url string starting with the 0b0111 nibble.
        var fid = root.GetProperty("fid").GetString();
        Assert.False(string.IsNullOrEmpty(fid));
    }

    [Fact]
    public async Task InstallAsync_NullToken_Throws()
    {
        // GetProperty("token") succeeds but GetString() returns null for a JSON null value,
        // which triggers the ?? throw InvalidOperationException arm.
        var handler = StubHttpMessageHandler.Always(HttpStatusCode.OK, """{ "authToken": { "token": null } }""");
        var register = new AndroidFcmRegister(handler.CreateClient());
        await Assert.ThrowsAsync<InvalidOperationException>(() => register.InstallAsync());
    }

    [Fact]
    public async Task RegisterFcmAsync_Success_ReturnsTokenAfterEquals()
    {
        var handler = StubHttpMessageHandler.Always(HttpStatusCode.OK, "token=the-fcm-token");
        var register = new AndroidFcmRegister(handler.CreateClient());

        var token = await register.RegisterFcmAsync(
            new RustPlusApi.Fcm.Data.Gcm
            {
                AndroidId = 42, SecurityToken = 99
            }, "fis-auth-token");

        Assert.Equal("the-fcm-token", token);

        var request = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(RegistrationConstants.FcmRegisterUrl, request.RequestUri!.ToString());

        // Authorization header carries "AidLogin {androidId}:{securityToken}".
        Assert.Equal("AidLogin 42:99", SingleHeader(request, "Authorization"));
        Assert.Equal("application/x-www-form-urlencoded", request.Content!.Headers.ContentType!.MediaType);

        // Exact c2dm form fields.
        var form = ParseForm(handler.RequestBodies[0]);
        Assert.Equal("42", form["device"]);
        Assert.Equal(RegistrationConstants.AndroidPackageName, form["app"]);
        Assert.Equal(RegistrationConstants.AndroidPackageCert, form["cert"]);
        Assert.Equal("1", form["app_ver"]);
        Assert.Equal(RegistrationConstants.GcmSenderId, form["X-subtype"]);
        Assert.Equal("1", form["X-app_ver"]);
        Assert.Equal("29", form["X-osv"]);
        Assert.Equal("fiid-21.1.1", form["X-cliv"]);
        Assert.Equal("220217001", form["X-gmsv"]);
        Assert.Equal("*", form["X-scope"]);
        Assert.Equal("fis-auth-token", form["X-Goog-Firebase-Installations-Auth"]);
        Assert.Equal(RegistrationConstants.GmsAppId, form["X-gms_app_id"]);
        Assert.Equal("fire-abt/21.1.1 fire-installations/17.0.0 fire-android/ fire-core/20.3.1",
            form["X-Firebase-Client"]);
        Assert.Equal("R1dAH9Ui7M-ynoznwBdw01tLxhI", form["X-firebase-app-name-hash"]);
        Assert.Equal("FIS_v2", form["X-Goog-Firebase-Installations-Auth-Version"]);
        Assert.Equal(RegistrationConstants.GcmSenderId, form["sender"]);
        Assert.Equal("31", form["target_ver"]);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task RegisterFcmAsync_AllAttemptsError_Throws()
    {
        var handler = StubHttpMessageHandler.Always(HttpStatusCode.OK, "Error=PHONE_REGISTRATION_ERROR");
        var register = new AndroidFcmRegister(handler.CreateClient());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            register.RegisterFcmAsync(new RustPlusApi.Fcm.Data.Gcm
            {
                AndroidId = 1, SecurityToken = 2
            }, "fis"));
        Assert.Equal(5, handler.Requests.Count); // five attempts
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task RegisterFcmAsync_RetriesThenSucceeds()
    {
        var handler = new StubHttpMessageHandler((_, i) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(i < 2 ? "Error=RETRY" : "token=ok")
        });
        var register = new AndroidFcmRegister(handler.CreateClient());

        var token = await register.RegisterFcmAsync(new RustPlusApi.Fcm.Data.Gcm
        {
            AndroidId = 1, SecurityToken = 2
        }, "fis");

        Assert.Equal("ok", token);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task RegisterAsync_ChainsAllThreeSteps()
    {
        var handler = new StubHttpMessageHandler((req, _) =>
        {
            var url = req.RequestUri!.ToString();
            if (url.Contains("checkin", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(CheckinResponseBytes(5, 6))
                };
            }

            if (url.Contains("installations", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{ "authToken": { "token": "fis" } }""")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("token=final")
            };
        });
        var register = new AndroidFcmRegister(handler.CreateClient());

        var (gcm, fcmToken) = await register.RegisterAsync();

        Assert.Equal(5UL, gcm.AndroidId);
        Assert.Equal(6UL, gcm.SecurityToken);
        Assert.Equal("final", fcmToken);

        // The three steps hit the three endpoints in order.
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(RegistrationConstants.CheckinUrl, handler.Requests[0].RequestUri!.ToString());
        Assert.Equal(RegistrationConstants.FirebaseInstallationsUrl, handler.Requests[1].RequestUri!.ToString());
        Assert.Equal(RegistrationConstants.FcmRegisterUrl, handler.Requests[2].RequestUri!.ToString());
        // RegisterFcm uses the AndroidId from check-in in its Authorization header.
        Assert.Equal("AidLogin 5:6", SingleHeader(handler.Requests[2], "Authorization"));
    }

    [Fact]
    public void GenerateFirebaseId_ProducesUnpaddedBase64UrlWithLeadingFidNibble()
    {
        var id = AndroidFcmRegister.GenerateFirebaseId();

        // base64url: no '+', '/', or '=' padding.
        Assert.DoesNotContain('+', id);
        Assert.DoesNotContain('/', id);
        Assert.DoesNotContain('=', id);

        // 17 bytes => ceil(17/3)*4 = 24 base64 chars, minus 1 '=' pad stripped = 23 chars.
        Assert.Equal(23, id.Length);

        // Decode back (re-pad to base64) and assert the first byte's high nibble is 0b0111.
        var b64 = id.Replace('-', '+').Replace('_', '/') + "=";
        var bytes = Convert.FromBase64String(b64);
        Assert.Equal(17, bytes.Length);
        Assert.Equal(0b0111_0000, bytes[0] & 0b1111_0000);
    }

    /// <summary>
    /// Asserts that the buffer filled by RandomNumberGenerator is not all-zeros — kills the
    /// Statement mutation that removes the RandomNumberGenerator.Fill(buffer) call on NET10+.
    /// With the fill removed the buffer stays all-zeros, so the base64-encoded id would be
    /// deterministic (the only non-zero byte is byte[0] after the nibble-mask).
    /// </summary>
    [Fact]
    public void GenerateFirebaseId_IsDifferentAcrossTwoCalls()
    {
        // Two independent calls must produce different ids (with overwhelming probability if RNG
        // is actually invoked; they'd both produce the same zero-padded id if fill were removed).
        var id1 = AndroidFcmRegister.GenerateFirebaseId();
        var id2 = AndroidFcmRegister.GenerateFirebaseId();
        Assert.NotEqual(id1, id2);
    }

    /// <summary>
    /// Asserts that <see cref="AndroidFcmRegister.CheckInAsync"/> throws when the server returns
    /// a non-success status — kills the Statement mutation that removes EnsureSuccessStatusCode().
    /// </summary>
    [Fact]
    public async Task CheckInAsync_NonSuccessStatus_Throws()
    {
        var handler = StubHttpMessageHandler.Always(HttpStatusCode.InternalServerError, []);
        var register = new AndroidFcmRegister(handler.CreateClient());
        await Assert.ThrowsAsync<HttpRequestException>(() => register.CheckInAsync());
    }

    /// <summary>
    /// Asserts that <see cref="AndroidFcmRegister.InstallAsync"/> throws when the server returns
    /// a non-success status — kills the Statement mutation that removes EnsureSuccessStatusCode().
    /// </summary>
    [Fact]
    public async Task InstallAsync_NonSuccessStatus_Throws()
    {
        var handler = StubHttpMessageHandler.Always(HttpStatusCode.Unauthorized, "nope");
        var register = new AndroidFcmRegister(handler.CreateClient());
        await Assert.ThrowsAsync<HttpRequestException>(() => register.InstallAsync());
    }

    /// <summary>
    /// Asserts the EXACT exception message thrown when the FIS response contains no auth token —
    /// kills the String mutation that replaces the message literal with "".
    /// </summary>
    [Fact]
    public async Task InstallAsync_NullToken_ExceptionMessageIsNonEmpty()
    {
        var handler = StubHttpMessageHandler.Always(HttpStatusCode.OK, """{ "authToken": { "token": null } }""");
        var register = new AndroidFcmRegister(handler.CreateClient());
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => register.InstallAsync());
        Assert.False(string.IsNullOrEmpty(ex.Message));
        Assert.Contains("auth token", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Asserts the EXACT exception message thrown when all FCM registration attempts fail —
    /// kills the String mutation that replaces the message literal with "".
    /// </summary>
    [Fact]
    [Trait("Category", "Slow")]
    public async Task RegisterFcmAsync_AllAttemptsFail_ExceptionMessageIsNonEmpty()
    {
        var handler = StubHttpMessageHandler.Always(HttpStatusCode.OK, "Error=PHONE_REGISTRATION_ERROR");
        var register = new AndroidFcmRegister(handler.CreateClient());
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            register.RegisterFcmAsync(new RustPlusApi.Fcm.Data.Gcm
            {
                AndroidId = 1, SecurityToken = 2
            }, "fis"));
        Assert.False(string.IsNullOrEmpty(ex.Message));
        Assert.Contains("attempts", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
