using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using RustPlusApi.CredentialsWeb.Sessions;
using RustPlusApi.Fcm.Registration;
using Xunit;

namespace RustPlusApi.CredentialsWeb.UnitTests;

public sealed class SecretsAreNeverLoggedTests
{
    private const string SteamTokenSentinel = "SENTINEL-STEAM-TOKEN-b3a1f0c2";
    private const int PlayerTokenSentinel = 1928374650;

    private static async Task<(CredentialsWebFactory Factory, Session Session)> RunFullFlowAsync()
    {
        var factory = new CredentialsWebFactory();
        factory.Steps.PairingToReturn = new ServerPairing
        {
            Ip = "10.0.0.1",
            Port = 28082,
            PlayerId = 76561198249527954,
            PlayerToken = PlayerTokenSentinel,
            Name = "Test Server"
        };

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var store = factory.Services.GetRequiredService<SessionStore>();
        store.TryCreate("203.0.113.7", isLocal: true, out var session, out _);

        await client.GetAsync(new Uri(
            $"/callback/{session!.ReturnToken}?steamId=76561198249527954&token={SteamTokenSentinel}",
            UriKind.Relative));
        await session.BackgroundWork;

        await client.PostAsync(new Uri($"/api/sessions/{session.SessionId}/pairing", UriKind.Relative), null);
        await session.BackgroundWork;

        return (factory, session);
    }

    [Fact]
    public async Task TheHarnessActuallyCapturesLogs()
    {
        // Guards against the whole suite passing vacuously because nothing was ever captured.
        var (factory, _) = await RunFullFlowAsync();
        await using var _f = factory;

        Assert.NotEmpty(factory.Logs.Records);
    }

    [Fact]
    public async Task TheSteamTokenNeverReachesALogRecord()
    {
        var (factory, _) = await RunFullFlowAsync();
        await using var _f = factory;

        Assert.DoesNotContain(
            factory.Logs.Records,
            record => record.Contains(SteamTokenSentinel, StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheCallbackQueryStringNeverReachesALogRecord()
    {
        var (factory, _) = await RunFullFlowAsync();
        await using var _f = factory;

        Assert.DoesNotContain(
            factory.Logs.Records,
            record => record.Contains("token=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ThePlayerTokenNeverReachesALogRecord()
    {
        var (factory, session) = await RunFullFlowAsync();
        await using var _f = factory;

        Assert.Equal(SessionState.Paired, session.State);
        Assert.DoesNotContain(
            factory.Logs.Records,
            record => record.Contains(
                PlayerTokenSentinel.ToString(System.Globalization.CultureInfo.InvariantCulture),
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheExpoPushTokenNeverReachesALogRecord()
    {
        var (factory, _) = await RunFullFlowAsync();
        await using var _f = factory;

        Assert.DoesNotContain(
            factory.Logs.Records,
            record => record.Contains("ExponentPushToken", StringComparison.Ordinal));
    }
}
