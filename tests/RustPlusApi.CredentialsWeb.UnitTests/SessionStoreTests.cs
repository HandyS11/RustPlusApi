using Microsoft.Extensions.Time.Testing;
using RustPlusApi.CredentialsWeb;
using RustPlusApi.CredentialsWeb.Sessions;
using Xunit;

namespace RustPlusApi.CredentialsWeb.UnitTests;

public sealed class SessionStoreTests
{
    private static readonly DateTimeOffset Origin = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    private static (SessionStore Store, FakeTimeProvider Time) NewStore()
    {
        var time = new FakeTimeProvider(Origin);
        var options = new AppOptions
        {
            CreatedTtl = TimeSpan.FromMinutes(5)
        };
        return (new SessionStore(options, time), time);
    }

    [Fact]
    public void TryCreate_ReturnsCreatedSessionWithDistinctIds()
    {
        var (store, _) = NewStore();
        using var _s = store;

        Assert.True(store.TryCreate("203.0.113.7", isLocal: true, out var session, out var failure));

        Assert.Equal(SessionCreateFailure.None, failure);
        Assert.Equal(SessionState.Created, session.State);
        Assert.Equal("203.0.113.7", session.ClientIp);
        Assert.NotEqual(session.SessionId, session.ReturnToken);
        Assert.Equal(Origin.AddMinutes(5), session.ExpiresAt);
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void TryGet_FindsSessionBySessionId()
    {
        var (store, _) = NewStore();
        using var _s = store;
        store.TryCreate("203.0.113.7", isLocal: true, out var created, out _);

        Assert.True(store.TryGet(created!.SessionId, out var found));
        Assert.Same(created, found);
    }

    [Fact]
    public void TryGet_ReturnsFalse_ForUnknownId()
    {
        var (store, _) = NewStore();
        using var _s = store;

        Assert.False(store.TryGet("nope", out var found));
        Assert.Null(found);
    }

    [Fact]
    public void TryGet_DoesNotAcceptTheReturnToken()
    {
        var (store, _) = NewStore();
        using var _s = store;
        store.TryCreate("203.0.113.7", isLocal: true, out var created, out _);

        Assert.False(store.TryGet(created!.ReturnToken, out _));
    }

    [Fact]
    public void TryConsumeReturnToken_ReturnsSessionOnce()
    {
        var (store, _) = NewStore();
        using var _s = store;
        store.TryCreate("203.0.113.7", isLocal: true, out var created, out _);

        Assert.True(store.TryConsumeReturnToken(created!.ReturnToken, out var first));
        Assert.Same(created, first);

        Assert.False(store.TryConsumeReturnToken(created.ReturnToken, out var second));
        Assert.Null(second);
    }

    [Fact]
    public void TryConsumeReturnToken_ReturnsFalse_ForUnknownToken()
    {
        var (store, _) = NewStore();
        using var _s = store;

        Assert.False(store.TryConsumeReturnToken("unknown", out _));
    }

    [Fact]
    public void TryConsumeReturnToken_LeavesSessionRetrievable()
    {
        var (store, _) = NewStore();
        using var _s = store;
        store.TryCreate("203.0.113.7", isLocal: true, out var created, out _);

        store.TryConsumeReturnToken(created!.ReturnToken, out _);

        Assert.True(store.TryGet(created.SessionId, out _));
    }

    [Fact]
    public void Remove_DisposesAndForgetsTheSession()
    {
        var (store, _) = NewStore();
        using var _s = store;
        store.TryCreate("203.0.113.7", isLocal: true, out var created, out _);

        store.Remove(created!.SessionId);

        Assert.False(store.TryGet(created.SessionId, out _));
        Assert.True(created.Lifetime.IsCancellationRequested);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void Remove_IsSafeForUnknownId()
    {
        var (store, _) = NewStore();
        using var _s = store;

        store.Remove("unknown");

        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void SweepExpired_RemovesOnlyExpiredSessions()
    {
        var (store, time) = NewStore();
        using var _s = store;
        store.TryCreate("203.0.113.7", isLocal: true, out var stale, out _);
        time.Advance(TimeSpan.FromMinutes(6));
        store.TryCreate("203.0.113.8", isLocal: true, out var fresh, out _);

        var swept = store.SweepExpired();

        Assert.Equal(1, swept);
        Assert.False(store.TryGet(stale!.SessionId, out _));
        Assert.True(store.TryGet(fresh!.SessionId, out _));
    }

    [Fact]
    public void SweepExpired_AlsoInvalidatesTheReturnToken()
    {
        var (store, time) = NewStore();
        using var _s = store;
        store.TryCreate("203.0.113.7", isLocal: true, out var session, out _);
        time.Advance(TimeSpan.FromMinutes(6));

        store.SweepExpired();

        Assert.False(store.TryConsumeReturnToken(session!.ReturnToken, out _));
    }

    [Fact]
    public void SweepExpired_UsesTheStateSpecificTtl()
    {
        var (store, time) = NewStore();
        using var _s = store;
        store.TryCreate("203.0.113.7", isLocal: true, out var session, out _);

        // Authenticated sessions get SessionTtl (15 min), not this store's CreatedTtl (5 min).
        session!.Advance(SessionState.Authenticated, time.GetUtcNow().Add(TimeSpan.FromMinutes(15)));
        time.Advance(TimeSpan.FromMinutes(6));

        Assert.Equal(0, store.SweepExpired());
    }

    [Fact]
    public void Dispose_DisposesEverySession()
    {
        var (store, _) = NewStore();
        store.TryCreate("203.0.113.7", isLocal: true, out var session, out _);

        store.Dispose();

        Assert.True(session!.Lifetime.IsCancellationRequested);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void TryCreate_RecordsWhetherTheVisitorReachedTheAppLocally()
    {
        using var store = new SessionStore(new AppOptions(), new FakeTimeProvider());

        Assert.True(store.TryCreate("203.0.113.7", isLocal: false, out var remote, out _));
        Assert.False(remote.IsLocal);
    }

    [Fact]
    public void TryCreate_RecordsALocalVisitor()
    {
        using var store = new SessionStore(new AppOptions(), new FakeTimeProvider());

        Assert.True(store.TryCreate("127.0.0.1", isLocal: true, out var local, out _));
        Assert.True(local.IsLocal);
    }
}
