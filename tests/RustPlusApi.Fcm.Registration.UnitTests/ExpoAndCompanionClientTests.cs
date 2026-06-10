using RustPlusApi.Fcm.Registration;
using RustPlusApi.Fcm.Registration.Steps;
using RustPlusApi.Fcm.Registration.UnitTests.TestHelpers;
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace RustPlusApi.Fcm.Registration.UnitTests;

/// <summary>Offline coverage of the Expo token exchange and Rust Companion registration steps.</summary>
public class ExpoAndCompanionClientTests
{
    [Fact]
    public async Task ExpoPushClient_GetTokenAsync_ReturnsToken()
    {
        var handler = StubHttpMessageHandler.Always(HttpStatusCode.OK,
            """{ "data": { "expoPushToken": "ExponentPushToken[xyz]" } }""");
        var client = new ExpoPushClient(handler.CreateClient());

        var token = await client.GetTokenAsync("fcm-token");

        Assert.Equal("ExponentPushToken[xyz]", token);

        var request = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(RegistrationConstants.ExpoPushTokenUrl, request.RequestUri!.ToString());
        Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);

        // Exact request BODY fields.
        using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(handler.RequestBodies[0]));
        var root = doc.RootElement;
        Assert.Equal("fcm", root.GetProperty("type").GetString());
        Assert.False(root.GetProperty("development").GetBoolean());
        Assert.Equal(RegistrationConstants.AndroidPackageName, root.GetProperty("appId").GetString());
        Assert.Equal("fcm-token", root.GetProperty("deviceToken").GetString());
        Assert.Equal(RegistrationConstants.ExpoProjectId, root.GetProperty("projectId").GetString());
        // deviceId is a fresh GUID per call; assert it parses as one.
        Assert.True(Guid.TryParse(root.GetProperty("deviceId").GetString(), out _));
    }

    [Fact]
    public async Task ExpoPushClient_NullToken_Throws()
    {
        // GetProperty("expoPushToken") succeeds but GetString() returns null for a JSON null value,
        // which triggers the ?? throw InvalidOperationException arm.
        var handler = StubHttpMessageHandler.Always(HttpStatusCode.OK, """{ "data": { "expoPushToken": null } }""");
        var client = new ExpoPushClient(handler.CreateClient());
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetTokenAsync("fcm"));
    }

    [Fact]
    public async Task RustCompanionClient_RegisterAsync_PostsExpectedBody()
    {
        var handler = StubHttpMessageHandler.Always(HttpStatusCode.OK, "{}");
        var client = new RustCompanionClient(handler.CreateClient());

        await client.RegisterAsync("steam-token", "ExponentPushToken[xyz]");

        var request = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(RegistrationConstants.CompanionRegisterUrl, request.RequestUri!.ToString());
        Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);

        // Exact request BODY fields (default DeviceId = "RustPlusApi", PushKind = 3).
        using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(handler.RequestBodies[0]));
        var root = doc.RootElement;
        Assert.Equal("steam-token", root.GetProperty("AuthToken").GetString());
        Assert.Equal("RustPlusApi", root.GetProperty("DeviceId").GetString());
        Assert.Equal(3, root.GetProperty("PushKind").GetInt32());
        Assert.Equal("ExponentPushToken[xyz]", root.GetProperty("PushToken").GetString());
    }

    [Fact]
    public async Task RustCompanionClient_RegisterAsync_UsesProvidedDeviceId()
    {
        var handler = StubHttpMessageHandler.Always(HttpStatusCode.OK, "{}");
        var client = new RustCompanionClient(handler.CreateClient());

        await client.RegisterAsync("steam-token", "ExponentPushToken[xyz]", deviceId: "my-device");

        using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(handler.RequestBodies[0]));
        Assert.Equal("my-device", doc.RootElement.GetProperty("DeviceId").GetString());
    }

    [Fact]
    public async Task RustCompanionClient_ErrorStatus_Throws()
    {
        var handler = StubHttpMessageHandler.Always(HttpStatusCode.Unauthorized, "nope");
        var client = new RustCompanionClient(handler.CreateClient());
        await Assert.ThrowsAsync<HttpRequestException>(() => client.RegisterAsync("t", "e"));
    }

    /// <summary>
    /// Asserts that <see cref="ExpoPushClient.GetTokenAsync"/> throws on a non-success response —
    /// kills the Statement mutation that removes httpResponse.EnsureSuccessStatusCode().
    /// </summary>
    [Fact]
    public async Task ExpoPushClient_NonSuccessStatus_Throws()
    {
        var handler = StubHttpMessageHandler.Always(HttpStatusCode.TooManyRequests, "slow down");
        var client = new ExpoPushClient(handler.CreateClient());
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetTokenAsync("fcm-token"));
    }

    /// <summary>
    /// Asserts the EXACT exception message when the Expo response has a null push token —
    /// kills the String mutation that replaces the message literal with "".
    /// </summary>
    [Fact]
    public async Task ExpoPushClient_NullToken_ExceptionMessageIsNonEmpty()
    {
        var handler = StubHttpMessageHandler.Always(HttpStatusCode.OK, """{ "data": { "expoPushToken": null } }""");
        var client = new ExpoPushClient(handler.CreateClient());
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetTokenAsync("fcm"));
        Assert.False(string.IsNullOrEmpty(ex.Message));
        Assert.Contains("push token", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
