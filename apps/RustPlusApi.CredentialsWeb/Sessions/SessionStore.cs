using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace RustPlusApi.CredentialsWeb.Sessions;

/// <summary>Why <see cref="SessionStore.TryCreate"/> refused.</summary>
internal enum SessionCreateFailure
{
    /// <summary>Creation succeeded.</summary>
    None = 0,

    /// <summary>The instance-wide session cap is full.</summary>
    GlobalLimit = 1,

    /// <summary>This address already holds a session past <see cref="SessionState.Created"/>.</summary>
    ActiveSessionForIp = 2,

    /// <summary>This address has completed too many flows in the last hour.</summary>
    HourlyLimit = 3
}

/// <summary>The in-memory session registry. There is no persistence anywhere in this app: a process
/// restart wipes every session by construction.</summary>
/// <param name="options">Caps and TTLs.</param>
/// <param name="timeProvider">Clock, injected so TTLs are testable.</param>
internal sealed class SessionStore(AppOptions options, TimeProvider timeProvider) : IDisposable
{
    private readonly ConcurrentDictionary<string, string> _byReturnToken = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Session> _bySessionId = new(StringComparer.Ordinal);

    /// <summary>Live sessions in any state.</summary>
    internal int Count => _bySessionId.Count;

    /// <summary>Disposes every live session.</summary>
    public void Dispose()
    {
        foreach (var sessionId in _bySessionId.Keys)
        {
            Remove(sessionId);
        }
    }

    /// <summary>Forgets a session and disposes it, invalidating its return token.</summary>
    /// <param name="sessionId">The session handle.</param>
    internal void Remove(string sessionId)
    {
        if (!_bySessionId.TryRemove(sessionId, out var session))
        {
            return;
        }

        _byReturnToken.TryRemove(session.ReturnToken, out _);
        session.Dispose();
    }

    /// <summary>Disposes every session whose expiry has passed. Returns how many were removed.</summary>
    internal int SweepExpired()
    {
        var now = timeProvider.GetUtcNow();
        var swept = 0;

        foreach (var (sessionId, session) in _bySessionId)
        {
            if (!session.IsExpired(now))
            {
                continue;
            }

            Remove(sessionId);
            swept++;
        }

        return swept;
    }

    /// <summary>Looks a session up by its return token and invalidates that token, so a callback URL
    /// replayed from browser history finds nothing.</summary>
    /// <param name="returnToken">The token from the callback path.</param>
    /// <param name="session">The owning session when the token was live.</param>
    internal bool TryConsumeReturnToken(string returnToken, [NotNullWhen(true)] out Session? session)
    {
        session = null;
        return _byReturnToken.TryRemove(returnToken, out var sessionId)
            && _bySessionId.TryGetValue(sessionId, out session);
    }

    /// <summary>Creates a session for <paramref name="clientIp"/>, or explains why it could not.</summary>
    /// <param name="clientIp">The caller's address.</param>
    /// <param name="session">The new session on success.</param>
    /// <param name="failure">Why creation was refused.</param>
    internal bool TryCreate(
        string clientIp,
        [NotNullWhen(true)] out Session? session,
        out SessionCreateFailure failure)
    {
        session = null;

        if (_bySessionId.Count >= options.MaxConcurrentSessions)
        {
            failure = SessionCreateFailure.GlobalLimit;
            return false;
        }

        var created = new Session(
            SessionIds.New(),
            SessionIds.New(),
            clientIp,
            timeProvider.GetUtcNow().Add(options.CreatedTtl));

        _bySessionId[created.SessionId] = created;
        _byReturnToken[created.ReturnToken] = created.SessionId;

        session = created;
        failure = SessionCreateFailure.None;
        return true;
    }

    /// <summary>Looks a session up by its handle. The return token is deliberately not accepted here.</summary>
    /// <param name="sessionId">The session handle.</param>
    /// <param name="session">The session when found.</param>
    internal bool TryGet(string sessionId, [NotNullWhen(true)] out Session? session) =>
        _bySessionId.TryGetValue(sessionId, out session);
}
