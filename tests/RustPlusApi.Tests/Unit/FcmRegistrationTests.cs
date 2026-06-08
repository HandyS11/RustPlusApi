using System.Net;
using System.Text;
using System.Text.Json;
using ProtoBuf;
using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Registration;
using RustPlusApi.Fcm.Registration.Protobuf;
using RustPlusApi.Tests.TestHelpers;
using Xunit;

namespace RustPlusApi.Tests.Unit;

/// <summary>Offline coverage of the credential-acquisition orchestration (steps 1–4) and the
/// missing-Expo-token guard on the interactive step.</summary>
public class FcmRegistrationTests
{
    [Fact]
    public async Task AcquireCredentialsAsync_ReturnsAssembledCredentials()
    {
        var handler = new StubHttpMessageHandler((req, _) =>
        {
            var url = req.RequestUri!.ToString();
            if (url.Contains("checkin", StringComparison.Ordinal))
            {
                using var ms = new MemoryStream();
                Serializer.Serialize(ms, new AndroidCheckinResponse { StatsOk = true, AndroidId = 9, SecurityToken = 8 });
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(ms.ToArray()) };
            }
            if (url.Contains("installations", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{ "authToken": { "token": "fis" } }""") };
            if (url.Contains("register", StringComparison.Ordinal)) // FCM c2dm register
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("token=fcm") };
            // Expo
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{ "data": { "expoPushToken": "ExponentPushToken[e]" } }""") };
        });
        var registration = new FcmRegistration(handler.CreateClient());

        var credentials = await registration.AcquireCredentialsAsync();

        Assert.Equal(9UL, credentials.Gcm.AndroidId);
        Assert.Equal(8UL, credentials.Gcm.SecurityToken);
        Assert.Equal("fcm", credentials.Fcm!.Token);
        Assert.Equal("ExponentPushToken[e]", credentials.ExpoPushToken);

        // The four credential-acquisition steps hit their endpoints in order.
        Assert.Equal(4, handler.Requests.Count);
        Assert.Equal(RegistrationConstants.CheckinUrl, handler.Requests[0].RequestUri!.ToString());
        Assert.Equal(RegistrationConstants.FirebaseInstallationsUrl, handler.Requests[1].RequestUri!.ToString());
        Assert.Equal(RegistrationConstants.FcmRegisterUrl, handler.Requests[2].RequestUri!.ToString());
        Assert.Equal(RegistrationConstants.ExpoPushTokenUrl, handler.Requests[3].RequestUri!.ToString());

        // The FCM token produced by step 3 is what step 4 (Expo) sends as its deviceToken.
        using var expoDoc = JsonDocument.Parse(Encoding.UTF8.GetString(handler.RequestBodies[3]));
        Assert.Equal("fcm", expoDoc.RootElement.GetProperty("deviceToken").GetString());
    }

    [Fact]
    public async Task RegisterWithRustPlusAsync_MissingExpoToken_Throws()
    {
        var registration = new FcmRegistration();
        var credentials = new Credentials { Gcm = new Gcm { AndroidId = 1, SecurityToken = 1 }, ExpoPushToken = null };
        await Assert.ThrowsAsync<InvalidOperationException>(() => registration.RegisterWithRustPlusAsync(credentials));
    }
}
