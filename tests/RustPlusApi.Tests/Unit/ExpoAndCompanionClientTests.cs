using System.Net;
using System.Text;
using RustPlusApi.Fcm.Registration.Steps;
using RustPlusApi.Tests.TestHelpers;
using Xunit;

namespace RustPlusApi.Tests.Unit;

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
        var body = Encoding.UTF8.GetString(handler.RequestBodies[0]);
        Assert.Contains("fcm-token", body, StringComparison.Ordinal);
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

        var body = Encoding.UTF8.GetString(handler.RequestBodies[0]);
        Assert.Contains("steam-token", body, StringComparison.Ordinal);
        Assert.Contains("ExponentPushToken[xyz]", body, StringComparison.Ordinal);
        Assert.Contains("\"PushKind\":3", body.Replace(" ", "", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RustCompanionClient_ErrorStatus_Throws()
    {
        var handler = StubHttpMessageHandler.Always(HttpStatusCode.Unauthorized, "nope");
        var client = new RustCompanionClient(handler.CreateClient());
        await Assert.ThrowsAsync<HttpRequestException>(() => client.RegisterAsync("t", "e"));
    }
}
