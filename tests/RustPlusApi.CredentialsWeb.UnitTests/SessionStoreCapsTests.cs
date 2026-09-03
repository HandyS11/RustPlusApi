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
    public void RecordCompletion_CountsAgain_AfterItsAddressWasPrunedAndUnlinked()
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
    public void RecordCompletion_NeverLosesAWrite_UnderConcurrentPruning()
    {
        // No clock manipulation: the vulnerable window in the pre-fix code is the gap between
        // RecordCompletion's GetOrAdd returning a reference to an address's (still-empty,
        // not-yet-populated) completions list and that same RecordCompletion call taking the
        // list's own lock to add to it. A concurrent CountCompletionsUnderGate call (driven here
        // by TryCreate, its only caller) can observe the list as empty in that gap and unlink it
        // from the map, so the pending write lands in an orphaned list nothing can reach again.
        //
        // The window is nanoseconds wide on any single attempt, so a single racer pair essentially
        // never hits it (verified empirically). This instead runs many "storms": one burst of
        // dedicated OS threads (not the thread pool, whose queuing lets the two sides drift out of
        // phase) pulling work off shared counters, where several recorder threads and several
        // pruner threads hammer the *same*, never-before-seen addresses at once, oversubscribing
        // the machine's cores to maximize preemption inside the tiny window. Measured against a
        // scratch revert of the fix (see the fix report), 70 storms at these sizes caught the loss
        // in 9 of 10 runs in roughly a second — repeating drives the odds up further without an
        // unbounded loop; it can never produce a false failure against the fixed code (verified
        // over dozens of runs), so there is no flakiness downside to running it every time.
        //
        // With the per-address cap set to the exact number of recorders, a lost write is directly
        // observable afterward: the count CountCompletionsUnderGate reads can never exceed the
        // number of writes that actually landed, so the final TryCreate for an address hits
        // HourlyLimit only if every one of its recorders' writes survived.
        const int storms = 70;
        const int addressCount = 2_000;
        const int recordersPerAddress = 16;
        const int recorderThreadCount = 100;
        const int prunerThreadCount = 50;
        const int recordTotal = addressCount * recordersPerAddress;

        var lostWrites = new List<string>();

        for (var storm = 0; storm < storms; storm++)
        {
            var (store, _) = NewStore(o => o.MaxCompletionsPerIpPerHour = recordersPerAddress);
            using var _s = store;

            var addresses = new string[addressCount];
            for (var i = 0; i < addressCount; i++)
            {
                addresses[i] = $"stress-{storm}-{i}";
            }

            var nextRecordUnit = -1;
            var nextPruneAddress = -1;

            // Every worker blocks here until all of them exist and are waiting, then all are
            // released together — otherwise starting recorder threads before pruner threads (or
            // vice versa) hands one side a head start that skews away from the interleaving under
            // test.
            using var allReady = new CountdownEvent(recorderThreadCount + prunerThreadCount);
            using var go = new ManualResetEventSlim(initialState: false);

            void RecordWorker()
            {
                allReady.Signal();
                go.Wait();

                int unit;

                // Grouped, not round-robin: consecutive units target the *same* address, so
                // several recorder threads pile onto that one address's list lock at once instead
                // of spreading across distinct addresses — the queued contention a pruner thread
                // needs to cut in front of while the list is still empty.
                while ((unit = Interlocked.Increment(ref nextRecordUnit)) < recordTotal)
                {
                    store.RecordCompletion(addresses[unit / recordersPerAddress]);
                }
            }

            void PruneWorker()
            {
                allReady.Signal();
                go.Wait();

                int index;
                while ((index = Interlocked.Increment(ref nextPruneAddress)) < addressCount)
                {
                    if (store.TryCreate(addresses[index], out var session, out _))
                    {
                        store.Remove(session!.SessionId);
                    }
                }
            }

            var threads = new List<Thread>(recorderThreadCount + prunerThreadCount);
            for (var t = 0; t < recorderThreadCount; t++)
            {
                threads.Add(new Thread(RecordWorker));
            }

            for (var t = 0; t < prunerThreadCount; t++)
            {
                threads.Add(new Thread(PruneWorker));
            }

            foreach (var thread in threads)
            {
                thread.Start();
            }

            Assert.True(allReady.Wait(TimeSpan.FromSeconds(10)));
            go.Set();

            foreach (var thread in threads)
            {
                Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
            }

            foreach (var address in addresses)
            {
                if (store.TryCreate(address, out _, out var failure))
                {
                    lostWrites.Add(address);
                    continue;
                }

                Assert.Equal(SessionCreateFailure.HourlyLimit, failure);
            }
        }

        Assert.Empty(lostWrites);
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
