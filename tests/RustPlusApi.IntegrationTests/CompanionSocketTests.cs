using RustPlusApi.MockServer;
using Xunit;

namespace RustPlusApi.IntegrationTests;

/// <summary>
/// The routes that reach Facepunch's companion host — the companion socket and the legacy proxy —
/// dialled against the in-process mock through <see cref="RustPlusSocket.CompanionBaseUriForTests"/>,
/// so the handshake the client actually sends can be asserted without any live network call.
/// </summary>
public class CompanionSocketTests
{
    private static readonly Guid ServerId = new("089941d9-064e-4628-9c60-eaeb01a943e8");
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ConnectAsync_WithServerId_DialsTheCompanionSocketWithTheAuthSubProtocols()
    {
        await using var server = new MockRustPlusServer();
        server.Start();

        // Server/Port name a host that does not exist: with ServerId set they must not be dialled.
        await using var client = new RustPlus(new RustPlusConnection("example.invalid", 28083, 1, 1)
        {
            ServerId = ServerId, AuthToken = "a?b>"
        })
        {
            CompanionBaseUriForTests = new Uri($"ws://{MockRustPlusServer.Host}:{server.Port}/")
        };

        await client.ConnectAsync().WaitAsync(Timeout);
        var response = await client.GetInfoAsync().WaitAsync(Timeout);

        Assert.True(response.IsSuccess);
        var handshake = Assert.IsType<MockHandshake>(server.LastHandshake);
        Assert.Equal("/socket/089941d9-064e-4628-9c60-eaeb01a943e8", handshake.Path);

        // "a?b>" is YT9iPg== in standard base64; the token travels unpadded and URL-safe.
        Assert.Equal(["rustplus.auth.v1", "YT9iPg"], handshake.SubProtocols);
    }

    [Fact]
    public async Task ConnectAsync_WithFacepunchProxy_DialsTheLegacyGamePathWithoutSubProtocols()
    {
        await using var server = new MockRustPlusServer();
        server.Start();
        await using var client =
            new RustPlus(new RustPlusConnection("12.34.56.78", 28083, 1, 1, UseFacepunchProxy: true))
            {
                CompanionBaseUriForTests = new Uri($"ws://{MockRustPlusServer.Host}:{server.Port}/")
            };

        await client.ConnectAsync().WaitAsync(Timeout);

        var handshake = Assert.IsType<MockHandshake>(server.LastHandshake);
        Assert.Equal("/game/12.34.56.78/28083", handshake.Path);
        Assert.Empty(handshake.SubProtocols);
    }

    [Fact]
    public async Task ConnectAsync_Direct_NeverOffersTheAuthToken()
    {
        // The auth token is for Facepunch's companion host only: a direct connection goes to whatever
        // the game server's app port is, so it must not carry the token even when one is set.
        await using var server = new MockRustPlusServer();
        server.Start();
        await using var client = new RustPlus(new RustPlusConnection(MockRustPlusServer.Host, server.Port, 1, 1)
        {
            AuthToken = "secret-companion-token"
        });

        await client.ConnectAsync().WaitAsync(Timeout);

        var handshake = Assert.IsType<MockHandshake>(server.LastHandshake);
        Assert.Equal("/", handshake.Path);
        Assert.Empty(handshake.SubProtocols);
    }
}
