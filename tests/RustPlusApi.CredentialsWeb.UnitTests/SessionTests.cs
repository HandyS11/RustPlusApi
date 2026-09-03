using RustPlusApi.CredentialsWeb.Sessions;
using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Registration;
using Xunit;

namespace RustPlusApi.CredentialsWeb.UnitTests;

public sealed class SessionTests
{
    private static readonly DateTimeOffset Origin = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    private static Session NewSession() =>
        new("session-id", "return-token", "203.0.113.7", Origin.AddMinutes(5));

    private static async Task<List<SessionEvent>> ReadAsync(Session session, int expected)
    {
        var received = new List<SessionEvent>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await foreach (var item in session.Events.SubscribeAsync(timeout.Token))
        {
            received.Add(item);
            if (received.Count == expected)
            {
                break;
            }
        }

        return received;
    }

    [Fact]
    public void New_StartsInCreatedState()
    {
        using var session = NewSession();

        Assert.Equal(SessionState.Created, session.State);
        Assert.Equal(Origin.AddMinutes(5), session.ExpiresAt);
        Assert.Equal("203.0.113.7", session.ClientIp);
    }

    [Fact]
    public async Task Advance_UpdatesStateAndExpiry_AndPublishesStepEvent()
    {
        using var session = NewSession();

        session.Advance(SessionState.Registering, Origin.AddMinutes(15));

        Assert.Equal(SessionState.Registering, session.State);
        Assert.Equal(Origin.AddMinutes(15), session.ExpiresAt);

        var events = await ReadAsync(session, 1);
        Assert.Equal("step", events[0].Type);
    }

    [Fact]
    public void SetSteamLogin_StoresIdAndToken()
    {
        using var session = NewSession();

        session.SetSteamLogin(new SteamLoginResult(76561198249527954, "steam-token"));

        Assert.Equal(76561198249527954UL, session.SteamId);
        Assert.Equal("steam-token", session.SteamToken);
    }

    [Fact]
    public void ClearSteamToken_DropsTokenButKeepsSteamId()
    {
        using var session = NewSession();
        session.SetSteamLogin(new SteamLoginResult(76561198249527954, "steam-token"));

        session.ClearSteamToken();

        Assert.Null(session.SteamToken);
        Assert.Equal(76561198249527954UL, session.SteamId);
    }

    [Fact]
    public void SetCredentialsAndPairing_AreExposed()
    {
        using var session = NewSession();
        var credentials = new Credentials
        {
            ExpoPushToken = "expo-token"
        };
        var pairing = new ServerPairing
        {
            Ip = "10.0.0.1", Port = 28082, PlayerId = 1, PlayerToken = 2
        };

        session.SetCredentials(credentials);
        session.SetPairing(pairing);

        Assert.Same(credentials, session.Credentials);
        Assert.Same(pairing, session.Pairing);
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(6, true)]
    public void IsExpired_ComparesAgainstExpiry(int minutes, bool expected)
    {
        using var session = NewSession();

        Assert.Equal(expected, session.IsExpired(Origin.AddMinutes(minutes)));
    }

    [Fact]
    public void Dispose_ClearsSecretsAndCancelsLifetime()
    {
        var session = NewSession();
        session.SetSteamLogin(new SteamLoginResult(1, "steam-token"));
        session.SetCredentials(new Credentials
        {
            ExpoPushToken = "expo-token"
        });
        session.SetPairing(new ServerPairing
        {
            Ip = "10.0.0.1", Port = 1, PlayerId = 1, PlayerToken = 2
        });

        session.Dispose();

        Assert.Null(session.SteamToken);
        Assert.Null(session.Credentials);
        Assert.Null(session.Pairing);
        Assert.True(session.Lifetime.IsCancellationRequested);
    }

    [Fact]
    public async Task Dispose_CompletesTheEventStream()
    {
        var session = NewSession();
        session.Dispose();

        var received = new List<SessionEvent>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var item in session.Events.SubscribeAsync(timeout.Token))
        {
            received.Add(item);
        }

        Assert.Empty(received);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var session = NewSession();

        session.Dispose();
        session.Dispose();

        Assert.True(session.Lifetime.IsCancellationRequested);
    }

    [Fact]
    public void Dispose_DoesNotThrow_WhenACancellationCallbackThrows()
    {
        var session = NewSession();
        session.Lifetime.Token.Register(() => throw new InvalidOperationException("Simulated callback failure"));

        var exception = Record.Exception(session.Dispose);

        Assert.Null(exception);
    }

    [Fact]
    public async Task Dispose_StillCompletesEventsAndDisposesLifetime_WhenACancellationCallbackThrows()
    {
        var session = NewSession();
        session.Lifetime.Token.Register(() => throw new InvalidOperationException("Simulated callback failure"));

        session.Dispose();

        // Events.Complete() ran despite Cancel() throwing: the stream is closed rather than left
        // open for a subscriber that would otherwise never see it end.
        var received = new List<SessionEvent>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var item in session.Events.SubscribeAsync(timeout.Token))
        {
            received.Add(item);
        }

        Assert.Empty(received);

        // Lifetime.Dispose() ran too: a disposed CancellationTokenSource throws on Token access.
        Assert.Throws<ObjectDisposedException>(() => _ = session.Lifetime.Token);
    }

    [Fact]
    public void SetSteamLogin_AfterDispose_DoesNotResurrectToken()
    {
        var session = NewSession();
        session.Dispose();

        session.SetSteamLogin(new SteamLoginResult(76561198249527954, "steam-token"));

        Assert.Null(session.SteamToken);
    }

    [Fact]
    public void SetCredentials_AfterDispose_DoesNotResurrectCredentials()
    {
        var session = NewSession();
        session.Dispose();

        session.SetCredentials(new Credentials
        {
            ExpoPushToken = "expo-token"
        });

        Assert.Null(session.Credentials);
    }

    [Fact]
    public void SetPairing_AfterDispose_DoesNotResurrectPairing()
    {
        var session = NewSession();
        session.Dispose();

        session.SetPairing(new ServerPairing
        {
            Ip = "10.0.0.1", Port = 1, PlayerId = 1, PlayerToken = 2
        });

        Assert.Null(session.Pairing);
    }

    [Fact]
    public void Advance_AfterDispose_DoesNotChangeState()
    {
        var session = NewSession();
        session.Dispose();

        session.Advance(SessionState.Registering, Origin.AddMinutes(15));

        Assert.Equal(SessionState.Created, session.State);
    }

    [Fact]
    public void SessionIds_AreThirtyTwoLowercaseHexCharsAndUnique()
    {
        var first = SessionIds.New();
        var second = SessionIds.New();

        Assert.Equal(32, first.Length);
        Assert.Matches("^[0-9a-f]{32}$", first);
        Assert.NotEqual(first, second);
    }
}
