using System.Net;
using ProtoBuf;
using RustPlusApi.Fcm.Registration.Protobuf;
using RustPlusApi.Fcm.Registration.Steps;
using RustPlusApi.Tests.TestHelpers;
using Xunit;

namespace RustPlusApi.Tests.Unit;

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
    }

    [Fact]
    public async Task InstallAsync_ReturnsAuthToken()
    {
        var handler = StubHttpMessageHandler.Always(HttpStatusCode.OK,
            """{ "authToken": { "token": "fis-token" } }""");
        var register = new AndroidFcmRegister(handler.CreateClient());

        var token = await register.InstallAsync();

        Assert.Equal("fis-token", token);
        // Header shaping:
        Assert.True(handler.Requests[0].Headers.Contains("x-goog-api-key"));
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

        var token = await register.RegisterFcmAsync(new RustPlusApi.Fcm.Data.Gcm { AndroidId = 1, SecurityToken = 2 }, "fis");

        Assert.Equal("the-fcm-token", token);
        Assert.True(handler.Requests[0].Headers.Contains("Authorization"));
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task RegisterFcmAsync_AllAttemptsError_Throws()
    {
        var handler = StubHttpMessageHandler.Always(HttpStatusCode.OK, "Error=PHONE_REGISTRATION_ERROR");
        var register = new AndroidFcmRegister(handler.CreateClient());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            register.RegisterFcmAsync(new RustPlusApi.Fcm.Data.Gcm { AndroidId = 1, SecurityToken = 2 }, "fis"));
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

        var token = await register.RegisterFcmAsync(new RustPlusApi.Fcm.Data.Gcm { AndroidId = 1, SecurityToken = 2 }, "fis");

        Assert.Equal("ok", token);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task RegisterAsync_ChainsAllThreeSteps()
    {
        var handler = new StubHttpMessageHandler((req, _) =>
        {
            var url = req.RequestUri!.ToString();
            if (url.Contains("checkin", StringComparison.Ordinal)) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(CheckinResponseBytes(5, 6)) };
            if (url.Contains("installations", StringComparison.Ordinal)) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{ "authToken": { "token": "fis" } }""") };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("token=final") };
        });
        var register = new AndroidFcmRegister(handler.CreateClient());

        var (gcm, fcmToken) = await register.RegisterAsync();

        Assert.Equal(5UL, gcm.AndroidId);
        Assert.Equal("final", fcmToken);
    }
}
