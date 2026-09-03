using Microsoft.Extensions.Time.Testing;
using RustPlusApi.CredentialsWeb;
using RustPlusApi.CredentialsWeb.Sessions;
using Xunit;

namespace RustPlusApi.CredentialsWeb.UnitTests;

public sealed class SessionStoreCapsTests
{
    private static readonly DateTimeOffset Origin = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    private static (SessionStore Store, FakeTimeProvider Time) NewStore(Action<AppOptions>? configure = null)
    {
        var time = new FakeTimeProvider(Origin);
        var options = new AppOptions
        {
            PublicBaseUrl = "https://creds.example.org"
        };
        configure?.Invoke(options);
        return (new SessionStore(options, time), time);
    }

    [Fact]
    public void TryCreate_Refuses_WhenGlobalLimitReached()
    {
        var (store, _) = NewStore(o => o.MaxConcurrentSessions = 1);
        using var _s = store;
        store.TryCreate("203.0.113.7", out _, out _);

        Assert.False(store.TryCreate("203.0.113.8", out var session, out var failure));

        Assert.Null(session);
        Assert.Equal(SessionCreateFailure.GlobalLimit, failure);
    }

    [Fact]
    public void TryCreate_EvictsAbandonedCreatedSession_FromTheSameIp()
    {
        var (store, _) = NewStore();
        using var _s = store;
        store.TryCreate("203.0.113.7", out var abandoned, out _);

        Assert.True(store.TryCreate("203.0.113.7", out var replacement, out var failure));

        Assert.Equal(SessionCreateFailure.None, failure);
        Assert.NotSame(abandoned, replacement);
        Assert.False(store.TryGet(abandoned!.SessionId, out _));
        Assert.True(store.TryGet(replacement!.SessionId, out _));
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void TryCreate_Refuses_WhenTheSameIpHasAnAuthenticatedSession()
    {
        var (store, time) = NewStore();
        using var _s = store;
        store.TryCreate("203.0.113.7", out var active, out _);
        active!.Advance(SessionState.Authenticated, time.GetUtcNow().AddMinutes(15));

        Assert.False(store.TryCreate("203.0.113.7", out var session, out var failure));

        Assert.Null(session);
        Assert.Equal(SessionCreateFailure.ActiveSessionForIp, failure);
    }

    [Fact]
    public void TryCreate_IsUnaffectedByOtherAddresses()
    {
        var (store, time) = NewStore();
        using var _s = store;
        store.TryCreate("203.0.113.7", out var other, out _);
        other!.Advance(SessionState.Authenticated, time.GetUtcNow().AddMinutes(15));

        Assert.True(store.TryCreate("203.0.113.8", out _, out var failure));
        Assert.Equal(SessionCreateFailure.None, failure);
    }

    [Fact]
    public void TryCreate_Refuses_AfterTooManyCompletionsInAnHour()
    {
        var (store, _) = NewStore(o => o.MaxCompletionsPerIpPerHour = 2);
        using var _s = store;
        store.RecordCompletion("203.0.113.7");
        store.RecordCompletion("203.0.113.7");

        Assert.False(store.TryCreate("203.0.113.7", out _, out var failure));
        Assert.Equal(SessionCreateFailure.HourlyLimit, failure);
    }

    [Fact]
    public void TryCreate_Recovers_AfterTheHourlyWindowSlidesPast()
    {
        var (store, time) = NewStore(o => o.MaxCompletionsPerIpPerHour = 1);
        using var _s = store;
        store.RecordCompletion("203.0.113.7");
        Assert.False(store.TryCreate("203.0.113.7", out _, out _));

        time.Advance(TimeSpan.FromMinutes(61));

        Assert.True(store.TryCreate("203.0.113.7", out _, out var failure));
        Assert.Equal(SessionCreateFailure.None, failure);
    }

    [Fact]
    public void RecordCompletion_IsScopedToOneAddress()
    {
        var (store, _) = NewStore(o => o.MaxCompletionsPerIpPerHour = 1);
        using var _s = store;
        store.RecordCompletion("203.0.113.7");

        Assert.True(store.TryCreate("203.0.113.8", out _, out _));
    }

    [Fact]
    public void RecordCompletion_StillCounts_AfterItsAddressWasPrunedToEmptyAndUnlinked()
    {
        var (store, time) = NewStore(o => o.MaxCompletionsPerIpPerHour = 1);
        using var _s = store;
        store.RecordCompletion("203.0.113.7");

        time.Advance(TimeSpan.FromMinutes(61));

        // The stale entry is now more than an hour old. This TryCreate's hourly check prunes
        // "203.0.113.7"'s completions list to empty and unlinks it from the per-IP map — the exact
        // moment a same-address RecordCompletion must not lose its write.
        Assert.True(store.TryCreate("203.0.113.7", out var session, out _));
        store.Remove(session!.SessionId);

        store.RecordCompletion("203.0.113.7");

        Assert.False(store.TryCreate("203.0.113.7", out _, out var failure));
        Assert.Equal(SessionCreateFailure.HourlyLimit, failure);
    }

    [Fact]
    public void TryAcquirePairingSlot_HonoursTheGlobalPairingCap()
    {
        var (store, _) = NewStore(o => o.MaxConcurrentPairings = 2);
        using var _s = store;

        Assert.True(store.TryAcquirePairingSlot());
        Assert.True(store.TryAcquirePairingSlot());
        Assert.False(store.TryAcquirePairingSlot());
        Assert.Equal(2, store.ActivePairings);
    }

    [Fact]
    public void ReleasePairingSlot_FreesCapacity()
    {
        var (store, _) = NewStore(o => o.MaxConcurrentPairings = 1);
        using var _s = store;
        store.TryAcquirePairingSlot();

        store.ReleasePairingSlot();

        Assert.Equal(0, store.ActivePairings);
        Assert.True(store.TryAcquirePairingSlot());
    }

    [Fact]
    public void ReleasePairingSlot_NeverGoesNegative()
    {
        var (store, _) = NewStore();
        using var _s = store;

        store.ReleasePairingSlot();

        Assert.Equal(0, store.ActivePairings);
    }
}
