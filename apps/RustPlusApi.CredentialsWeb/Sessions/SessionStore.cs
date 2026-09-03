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

    private readonly ConcurrentDictionary<string, List<DateTimeOffset>> _completionsByIp =
        new(StringComparer.Ordinal);

    private readonly Lock _createGate = new();
    private int _activePairings;

    /// <summary>Live MCS sockets.</summary>
    internal int ActivePairings => Volatile.Read(ref _activePairings);

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

    /// <summary>Records that <paramref name="clientIp"/> finished a flow, for the rolling hourly cap.
    /// Runs under the same gate as <see cref="TryCreate"/> rather than a per-list lock: a per-list
    /// lock lets a completion race the exact moment <see cref="CountCompletionsUnderGate"/> prunes
    /// that address's list to empty and unlinks it from the map, silently losing the completion.
    /// A single gate removes that interleaving entirely instead of patching around it.</summary>
    /// <param name="clientIp">The caller's address.</param>
    internal void RecordCompletion(string clientIp)
    {
        lock (_createGate)
        {
            var stamps = _completionsByIp.GetOrAdd(clientIp, static _ => []);
            stamps.Add(timeProvider.GetUtcNow());
        }
    }

    /// <summary>Releases a pairing slot. Safe to call more often than it was acquired.</summary>
    internal void ReleasePairingSlot()
    {
        while (true)
        {
            var current = Volatile.Read(ref _activePairings);
            if (current == 0)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _activePairings, current - 1, current) == current)
            {
                return;
            }
        }
    }

    /// <summary>Takes one of the globally capped MCS socket slots.</summary>
    internal bool TryAcquirePairingSlot()
    {
        while (true)
        {
            var current = Volatile.Read(ref _activePairings);
            if (current >= options.MaxConcurrentPairings)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _activePairings, current + 1, current) == current)
            {
                return true;
            }
        }
    }

    /// <summary>Creates a session for <paramref name="clientIp"/>, or explains why it could not.
    /// A session from the same address that is still in <see cref="SessionState.Created"/> is
    /// evicted rather than blocking: a visitor who closed the tab and started over must not be
    /// locked out by their own abandoned attempt. An address holding a session past
    /// <see cref="SessionState.Created"/> is refused instead — real upstream work exists there,
    /// and that session is resumable via its handle.</summary>
    /// <param name="clientIp">The caller's address.</param>
    /// <param name="session">The new session on success.</param>
    /// <param name="failure">Why creation was refused.</param>
    internal bool TryCreate(
        string clientIp,
        [NotNullWhen(true)] out Session? session,
        out SessionCreateFailure failure)
    {
        session = null;

        lock (_createGate)
        {
            if (CountCompletionsUnderGate(clientIp) >= options.MaxCompletionsPerIpPerHour)
            {
                failure = SessionCreateFailure.HourlyLimit;
                return false;
            }

            foreach (var (sessionId, existing) in _bySessionId)
            {
                if (!string.Equals(existing.ClientIp, clientIp, StringComparison.Ordinal))
                {
                    continue;
                }

                if (existing.State != SessionState.Created)
                {
                    failure = SessionCreateFailure.ActiveSessionForIp;
                    return false;
                }

                Remove(sessionId);
            }

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
    }

    /// <summary>Looks a session up by its handle. The return token is deliberately not accepted here.</summary>
    /// <param name="sessionId">The session handle.</param>
    /// <param name="session">The session when found.</param>
    internal bool TryGet(string sessionId, [NotNullWhen(true)] out Session? session) =>
        _bySessionId.TryGetValue(sessionId, out session);

    /// <summary>Completions by this address inside the trailing hour, pruning older entries as it goes.
    /// This bounds an address's own list only when that address calls <see cref="TryCreate"/> again —
    /// it is the only caller, so an address that keeps calling <see cref="RecordCompletion"/> without
    /// ever retrying <see cref="TryCreate"/> keeps growing its list, since nothing else visits it to
    /// prune. Must be called while already holding <see cref="_createGate"/>: it does no locking of
    /// its own and relies on the caller for exclusive access to <see cref="_completionsByIp"/>.</summary>
    /// <param name="clientIp">The caller's address.</param>
    private int CountCompletionsUnderGate(string clientIp)
    {
        if (!_completionsByIp.TryGetValue(clientIp, out var stamps))
        {
            return 0;
        }

        var cutoff = timeProvider.GetUtcNow().AddHours(-1);
        stamps.RemoveAll(stamp => stamp < cutoff);
        if (stamps.Count == 0)
        {
            _completionsByIp.TryRemove(clientIp, out _);
        }

        return stamps.Count;
    }
}
