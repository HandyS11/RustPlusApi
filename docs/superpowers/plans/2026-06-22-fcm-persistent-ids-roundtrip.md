# FCM persistentIds round-trip Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give FCM consumers a clean, discoverable way to read back the server-assigned `persistentId`s the socket harvests, so they can persist them and avoid duplicate notifications when recreating the socket.

**Architecture:** Today `persistentIds` flows IN via a constructor parameter the library mutates in place and then `Clear()`s on login-response — there is no OUT path, and the clear destroys the caller's seeded history. This plan adds a read-only snapshot property (`PersistentIds`) and a per-id event (`PersistentIdReceived`) to both `RustPlusFcmSocket` and `IRustPlusFcmSocket`, and removes the `Clear()` so the caller-owned set survives reconnect. Eviction/TTL stays the caller's responsibility (documented).

**Tech Stack:** C# multi-targeting netstandard2.0 + net10.0, xUnit (run on net8.0 + net10.0 hosts), protobuf-net (code-first MCS contracts), source-generated logging.

## Global Constraints

- All `src/` libraries multi-target **netstandard2.0 + net10.0**; no API may use a net10-only type without a polyfill/`#if` fork. The new members use only `ICollection<string>` / `IReadOnlyCollection<string>` / `EventHandler<string>` (all present on both TFMs).
- Build is strict: **TreatWarningsAsErrors** + latest-all analyzers (Roslynator, Sonar, VSTHRD). Zero warnings.
- Tests must pass on **BOTH** TFM hosts: `dotnet test RustPlusApi.sln` (net8.0 exercises the netstandard2.0 build, net10.0 the net10 build).
- Non-excluded FCM code is expected at **100/100 line/branch**; new code must be fully covered. CI gate: line ≥ 95 / branch ≥ 90 (`tools/coverage/report.sh`).
- Do NOT bump versions in project files (CD injects them).
- Format before any push: `dotnet jb cleanupcode RustPlusApi.sln --profile="ReformatAndReorder"`. The pre-push hook rejects unformatted outgoing files.
- Stryker cannot mutate the core `RustPlusApi.csproj`, but it CAN mutate `RustPlusApi.Fcm.csproj` — the `Clear()` line is currently pinned by a mutation-kill test (`LoginResponse_DispatchedViaOnGotMessageBytes_ClearsPersistentIds`). Removing `Clear()` requires giving that test a NEW observable side-effect of LoginResponse dispatch (see Task 3).
- No auto-commit/push beyond what this plan's steps specify; the user runs the final push/PR themselves unless they ask otherwise.

---

## File Structure

| File | Responsibility | Change |
|---|---|---|
| `src/RustPlusApi.Fcm/RustPlusFcmSocket.cs` | The MCS socket: harvest, dedup, expose IDs | Add `PersistentIds` property + `PersistentIdReceived` event; raise event on harvest; remove `Clear()` on login-response; update ctor doc |
| `src/RustPlusApi.Fcm/Interfaces/IRustPlusFcmSocket.cs` | Public socket contract | Add `PersistentIds` + `PersistentIdReceived` to the interface |
| `src/RustPlusApi.Fcm/RustPlusFcm.cs` | Derived listener; only its ctor doc mentions persistentIds | Update ctor doc to point at the new OUT path |
| `src/RustPlusApi.Fcm.Extensions.DependencyInjection/IRustPlusFcmFactory.cs` | Factory contract doc | Refresh the persistentIds doc (no behavior change) |
| `tests/RustPlusApi.Fcm.UnitTests/FcmSocketFramingTests.cs` | Pins socket framing/dedup/lifecycle behavior | Add new tests; rewrite the 3 clear-dependent tests |
| `samples/RustPlus.Fcm.ConsoleApp/Program.cs` | Demonstrates a listener | Add save-on-shutdown / load-on-startup round-trip using the new API |

---

### Task 1: Expose harvested ids (snapshot property + per-id event)

**Files:**

- Modify: `src/RustPlusApi.Fcm/RustPlusFcmSocket.cs` (events block ~L105-141; `OnDataMessage` add at ~L814-817)
- Modify: `src/RustPlusApi.Fcm/Interfaces/IRustPlusFcmSocket.cs`
- Test: `tests/RustPlusApi.Fcm.UnitTests/FcmSocketFramingTests.cs`

**Interfaces:**

- Consumes: existing `private readonly ICollection<string>? persistentIds` primary-ctor param; existing `OnDataMessage(DataMessageStanza?)`; existing test helpers `NewSocket(ICollection<string>?)`, `Build`, `FirstFrame`, `NextFrame`, `RustNotification(persistentId:)`, `ScriptedStream`, `RunReceiveLoopOverStreamAsync`.
- Produces:
  - `public IReadOnlyCollection<string> PersistentIds { get; }` on both `RustPlusFcmSocket` and `IRustPlusFcmSocket` — a snapshot of currently-tracked ids; empty (never null) when none/`null` was supplied.
  - `public event EventHandler<string>? PersistentIdReceived;` on both — raised once per newly-harvested id, AFTER it is added to the set, with the id string as the event arg.

- [ ] **Step 1: Write the failing tests**

Add to `FcmSocketFramingTests.cs` (inside the `FcmSocketFramingTests` class, near the other dedup tests ~L451):

```csharp
[Fact]
public async Task PersistentIdReceived_RaisedPerHarvestedId_AndSnapshotReflectsThem()
{
    var ids = new HashSet<string>();
    await using var socket = NewSocket(ids);
    var harvested = new List<string>();
    socket.PersistentIdReceived += (_, id) => harvested.Add(id);

    var script = Build(
        FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
        NextFrame(McsProtoTag.KDataMessageStanzaTag, RustNotification(persistentId: "id-1")),
        NextFrame(McsProtoTag.KDataMessageStanzaTag, RustNotification(persistentId: "id-2")),
        NextFrame(McsProtoTag.KCloseTag, new Close()));

    await socket.RunReceiveLoopOverStreamAsync(new ScriptedStream(script));

    // Event fired once per NEW id, in order.
    Assert.Equal(new[] { "id-1", "id-2" }, harvested);
    // Snapshot exposes the same ids (no Clear destroyed them).
    Assert.Equal(new[] { "id-1", "id-2" }, socket.PersistentIds.OrderBy(x => x));
}

[Fact]
public async Task PersistentIdReceived_NotRaisedForDuplicate()
{
    await using var socket = NewSocket([]);
    var harvested = new List<string>();
    socket.PersistentIdReceived += (_, id) => harvested.Add(id);

    var script = Build(
        FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
        NextFrame(McsProtoTag.KDataMessageStanzaTag, RustNotification(persistentId: "dup")),
        NextFrame(McsProtoTag.KDataMessageStanzaTag, RustNotification(persistentId: "dup")),
        NextFrame(McsProtoTag.KCloseTag, new Close()));

    await socket.RunReceiveLoopOverStreamAsync(new ScriptedStream(script));

    Assert.Equal(new[] { "dup" }, harvested); // duplicate did not re-raise
}

[Fact]
public void PersistentIds_NullCollection_SnapshotIsEmptyNotNull()
{
    using var socket = NewSocket(persistentIds: null);
    Assert.NotNull(socket.PersistentIds);
    Assert.Empty(socket.PersistentIds);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/RustPlusApi.Fcm.UnitTests/RustPlusApi.Fcm.UnitTests.csproj -f net10.0 --filter "FullyQualifiedName~PersistentId"`
Expected: COMPILE ERROR — `PersistentIds` / `PersistentIdReceived` not defined.

- [ ] **Step 3: Add the members to `RustPlusFcmSocket.cs`**

In the events region (after the `ErrorOccurred` event ~L141) add:

```csharp
    /// <summary>
    /// Occurs once for each newly-harvested FCM <c>persistentId</c>, immediately after it is added to
    /// the tracked set. Subscribe to persist ids incrementally so a crash or quick restart cannot
    /// reopen the redelivery window (the server only stops redelivering a message once its id is
    /// replayed in a later login's <c>ReceivedPersistentIds</c>).
    /// </summary>
    /// <remarks>The event data is the harvested <c>persistentId</c> as a <see cref="string"/>.</remarks>
    public event EventHandler<string>? PersistentIdReceived;

    /// <summary>
    /// A snapshot of the FCM <c>persistentId</c>s currently tracked for de-duplication — the ids
    /// supplied at construction plus every id harvested since. Persist these and pass them back into
    /// a new instance to suppress redelivery of already-processed messages across reconnects. The
    /// collection is never <see langword="null"/> (empty when no ids are tracked). Ids have a
    /// server-side lifespan; pruning your persisted copy is the caller's responsibility.
    /// </summary>
    public IReadOnlyCollection<string> PersistentIds =>
        persistentIds is null ? [] : [.. persistentIds];
```

Then in `OnDataMessage`, replace the harvest block (~L814-817):

```csharp
        if (dataMessage.PersistentId is not null)
        {
            persistentIds?.Add(dataMessage.PersistentId);
        }
```

with:

```csharp
        if (dataMessage.PersistentId is not null && persistentIds is not null)
        {
            persistentIds.Add(dataMessage.PersistentId);
            PersistentIdReceived?.Invoke(this, dataMessage.PersistentId);
        }
```

- [ ] **Step 4: Add the members to the interface `IRustPlusFcmSocket.cs`**

After the `ErrorOccurred` event:

```csharp
    /// <summary>Raised once per newly-harvested FCM <c>persistentId</c>, after it is tracked.
    /// Subscribe to persist ids incrementally and minimise the cross-session redelivery window.</summary>
    event EventHandler<string>? PersistentIdReceived;

    /// <summary>A never-null snapshot of the <c>persistentId</c>s currently tracked for
    /// de-duplication. Persist and replay these via the constructor to suppress redelivery across
    /// reconnects; ids have a server-side lifespan, so pruning your stored copy is your job.</summary>
    IReadOnlyCollection<string> PersistentIds { get; }
```

- [ ] **Step 5: Run the new tests to verify they pass**

Run: `dotnet test tests/RustPlusApi.Fcm.UnitTests/RustPlusApi.Fcm.UnitTests.csproj --filter "FullyQualifiedName~PersistentId"`
Expected: PASS on both net8.0 and net10.0. (Note: `LoginResponse_ClearsPreSeededPersistentIds` and `LoginResponse_DispatchedViaOnGotMessageBytes_ClearsPersistentIds` will still pass here because `Clear()` is still present — they are fixed in Tasks 2 & 3.)

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusApi.Fcm/RustPlusFcmSocket.cs src/RustPlusApi.Fcm/Interfaces/IRustPlusFcmSocket.cs tests/RustPlusApi.Fcm.UnitTests/FcmSocketFramingTests.cs
git commit -m "feat(fcm): expose harvested persistentIds via snapshot + event"
```

---

### Task 2: Stop clearing the caller's set on login-response

**Files:**

- Modify: `src/RustPlusApi.Fcm/RustPlusFcmSocket.cs` (`OnMessageAsync`, `KLoginResponseTag` arm ~L730-732)
- Test: `tests/RustPlusApi.Fcm.UnitTests/FcmSocketFramingTests.cs` (rewrite `LoginResponse_ClearsPreSeededPersistentIds`)

**Interfaces:**

- Consumes: `PersistentIds` property from Task 1.
- Produces: login-response no longer mutates the caller's `persistentIds`; seeded ids survive login and remain in `PersistentIds`.

- [ ] **Step 1: Rewrite the clear-assertion test to assert the OPPOSITE (preservation)**

Replace `LoginResponse_ClearsPreSeededPersistentIds` (~L857-885) with:

```csharp
    /// <summary>
    /// Asserts that LoginResponse does NOT clear the caller's persistentIds set: a message whose id
    /// was seeded BEFORE login is still de-duplicated (skipped) after login, and the seeded id
    /// survives in the public snapshot. The seeded ids have already been replayed to the server in
    /// the login request, so the caller's local history must be preserved for reconnect.
    /// </summary>
    [Fact]
    public async Task LoginResponse_PreservesPreSeededPersistentIds()
    {
        var ids = new HashSet<string> { "pre-existing-id" };
        await using var socket = NewSocket(ids);
        var count = 0;
        socket.NotificationReceived += (_, _) => count++;

        var script = Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            // Same id as the seed — must be SKIPPED because the set was NOT cleared.
            NextFrame(McsProtoTag.KDataMessageStanzaTag, RustNotification(persistentId: "pre-existing-id")),
            NextFrame(McsProtoTag.KCloseTag, new Close()));

        await socket.RunReceiveLoopOverStreamAsync(new ScriptedStream(script));

        Assert.Equal(0, count); // duplicate of a seeded id is suppressed
        Assert.Contains("pre-existing-id", socket.PersistentIds);
    }
```

- [ ] **Step 2: Run it to verify it FAILS against current behavior**

Run: `dotnet test tests/RustPlusApi.Fcm.UnitTests/RustPlusApi.Fcm.UnitTests.csproj -f net10.0 --filter "FullyQualifiedName~LoginResponse_PreservesPreSeededPersistentIds"`
Expected: FAIL — current `Clear()` empties the set, so the message is delivered (`count == 1`) instead of skipped.

- [ ] **Step 3: Remove the `Clear()`**

In `OnMessageAsync`, change the `KLoginResponseTag` arm (~L730-732) from:

```csharp
            case McsProtoTag.KLoginResponseTag:
                persistentIds?.Clear();
                break;
```

to:

```csharp
            case McsProtoTag.KLoginResponseTag:
                // Do NOT clear the caller's set: the seeded ids were already replayed to the server in
                // the login request (ReceivedPersistentIds), and the caller owns this collection for
                // cross-reconnect persistence. Clearing it would destroy their history.
                break;
```

- [ ] **Step 4: Run it to verify it PASSES**

Run: `dotnet test tests/RustPlusApi.Fcm.UnitTests/RustPlusApi.Fcm.UnitTests.csproj --filter "FullyQualifiedName~LoginResponse_PreservesPreSeededPersistentIds"`
Expected: PASS on both TFMs.

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusApi.Fcm/RustPlusFcmSocket.cs tests/RustPlusApi.Fcm.UnitTests/FcmSocketFramingTests.cs
git commit -m "fix(fcm): stop clearing caller's persistentIds on login-response"
```

---

### Task 3: Re-pin LoginResponse dispatch with a non-clear observable; fix the dup-test comment

**Files:**

- Modify: `tests/RustPlusApi.Fcm.UnitTests/FcmSocketFramingTests.cs` (`LoginResponse_DispatchedViaOnGotMessageBytes_ClearsPersistentIds` ~L944-970; `DuplicatePersistentId_IsSkipped` comment ~L436-437)

**Interfaces:**

- Consumes: existing `_streamIdIn` behavior — the receive loop increments the incoming stream position per dispatched frame; LoginResponse is frame #1. There is no direct getter, but the observable proxy is that AFTER a LoginResponse is dispatched, a subsequent DataMessage is processed normally (delivered). The previous test abused `Clear()` as the proof; we replace it with a proof that does not depend on clearing.

- [ ] **Step 1: Replace the mutation-kill test so it no longer relies on `Clear()`**

The old test proved "LoginResponse was dispatched" by observing that the pre-seeded set got cleared (so a same-id message was delivered). With clearing gone, prove dispatch a different way: if the LoginResponse frame were NOT dispatched, the first frame would be treated as the (required) login and a following DataMessage with a NEW id would still be delivered — but more directly, a NON-login first frame throws. We instead assert that a valid LoginResponse first frame lets a subsequent fresh-id DataMessage through (dispatch succeeded and did not fault), which kills the "remove the LoginResponse dispatch call" mutation because skipping dispatch of the first frame makes `RunReceiveLoopOverStreamAsync` treat it as a non-login first frame and raise.

Replace `LoginResponse_DispatchedViaOnGotMessageBytes_ClearsPersistentIds` (~L944-970) with:

```csharp
    /// <summary>
    /// Asserts the initial LoginResponse frame IS dispatched: a valid LoginResponse first frame must
    /// be consumed as the login (not faulted as a non-login first frame), after which a fresh-id
    /// DataMessage is delivered normally. Kills the Statement mutation that removes the first-frame
    /// dispatch call — without it, the first frame is mis-handled and no notification is delivered.
    /// </summary>
    [Fact]
    public async Task LoginResponse_IsDispatched_ThenDataMessageDelivered()
    {
        await using var socket = NewSocket([]);
        var count = 0;
        socket.NotificationReceived += (_, _) => count++;

        var script = Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            NextFrame(McsProtoTag.KDataMessageStanzaTag, RustNotification("fresh-id")),
            NextFrame(McsProtoTag.KCloseTag, new Close()));

        var exception = await Record.ExceptionAsync(
            () => socket.RunReceiveLoopOverStreamAsync(new ScriptedStream(script)));

        Assert.Null(exception);  // LoginResponse accepted as the login frame
        Assert.Equal(1, count);  // subsequent DataMessage delivered
    }
```

- [ ] **Step 2: Fix the now-stale comment in `DuplicatePersistentId_IsSkipped`**

In `DuplicatePersistentId_IsSkipped` (~L434-451), replace the comment lines (~L436-437):

```csharp
        // The LoginResponse handler clears the dedupe set, so seeding it up front would not survive.
        // Instead send the same PersistentId twice: the first populates the set, the second is skipped.
```

with:

```csharp
        // Send the same PersistentId twice within one session: the first harvests it into the set,
        // the second is recognised as a duplicate and skipped. (LoginResponse no longer clears the set.)
```

- [ ] **Step 3: Run the affected tests**

Run: `dotnet test tests/RustPlusApi.Fcm.UnitTests/RustPlusApi.Fcm.UnitTests.csproj --filter "FullyQualifiedName~LoginResponse_IsDispatched_ThenDataMessageDelivered|FullyQualifiedName~DuplicatePersistentId_IsSkipped"`
Expected: PASS on both TFMs.

- [ ] **Step 4: Run the full FCM unit-test project (regression sweep)**

Run: `dotnet test tests/RustPlusApi.Fcm.UnitTests/RustPlusApi.Fcm.UnitTests.csproj`
Expected: PASS on both net8.0 and net10.0, no skips.

- [ ] **Step 5: Commit**

```bash
git add tests/RustPlusApi.Fcm.UnitTests/FcmSocketFramingTests.cs
git commit -m "test(fcm): re-pin LoginResponse dispatch without relying on Clear()"
```

---

### Task 4: Refresh docs (constructor + factory) to point at the OUT path

**Files:**

- Modify: `src/RustPlusApi.Fcm/RustPlusFcmSocket.cs` (ctor `<param name="persistentIds">` doc ~L25-28)
- Modify: `src/RustPlusApi.Fcm/RustPlusFcm.cs` (ctor `<param name="persistentIds">` doc ~L15-18)
- Modify: `src/RustPlusApi.Fcm.Extensions.DependencyInjection/IRustPlusFcmFactory.cs` (`<param name="persistentIds">` doc ~L16-18)

**Interfaces:**

- Consumes: `PersistentIds` / `PersistentIdReceived` from Task 1.
- Produces: docs only — no behavior change.

- [ ] **Step 1: Update the `RustPlusFcmSocket` ctor doc**

Replace the `<param name="persistentIds">` block (~L25-28) with:

```csharp
/// <param name="persistentIds">Already-processed message ids, used for de-duplication, and the
/// collection the socket harvests new ids into — pass a mutable, caller-owned set (prefer a
/// <see cref="HashSet{T}"/>; a <see cref="List{T}"/> makes the duplicate check an O(n) scan). When
/// <see langword="null"/>, de-duplication is disabled. The set is NOT cleared on login, so seeded
/// ids survive reconnect. Read the current ids back via <see cref="PersistentIds"/> (snapshot) or
/// subscribe to <see cref="PersistentIdReceived"/> (incremental) to persist them; ids have a
/// server-side lifespan, so pruning your stored copy is your responsibility.</param>
```

- [ ] **Step 2: Update the `RustPlusFcm` ctor doc**

Replace its `<param name="persistentIds">` block (~L15-18) with the same text as Step 1 (the param has identical semantics on the derived type).

- [ ] **Step 3: Update the factory doc**

Replace `IRustPlusFcmFactory.Create`'s `<param name="persistentIds">` block (~L16-18) with:

```csharp
    /// <param name="persistentIds">Already-processed message ids to skip, and the set new ids are
    /// harvested into. When <see langword="null"/>, the factory supplies a fresh empty list, so
    /// in-session deduplication is always enabled (unlike the
    /// <see cref="RustPlusApi.Fcm.RustPlusFcm"/> constructor, where <see langword="null"/> disables
    /// it). Read ids back via <c>PersistentIds</c> / <c>PersistentIdReceived</c> on the returned
    /// listener to persist them across reconnects.</param>
```

- [ ] **Step 4: Build to confirm docs compile (XML-doc crefs resolve under strict build)**

Run: `dotnet build src/RustPlusApi.Fcm/RustPlusApi.Fcm.csproj src/RustPlusApi.Fcm.Extensions.DependencyInjection/RustPlusApi.Fcm.Extensions.DependencyInjection.csproj`
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusApi.Fcm/RustPlusFcmSocket.cs src/RustPlusApi.Fcm/RustPlusFcm.cs src/RustPlusApi.Fcm.Extensions.DependencyInjection/IRustPlusFcmFactory.cs
git commit -m "docs(fcm): document persistentIds round-trip via PersistentIds/PersistentIdReceived"
```

---

### Task 5: Demonstrate the round-trip in the FCM sample

**Files:**

- Modify: `samples/RustPlus.Fcm.ConsoleApp/Program.cs`

**Interfaces:**

- Consumes: `RustPlusFcm(credentials, persistentIds, ...)` ctor; `PersistentIdReceived` event; `PersistentIds` property.
- Produces: sample only — not covered by the gate, no tests.

- [ ] **Step 1: Load a persisted set on startup and pass it in**

After credentials are loaded and before `new RustPlusFcm(...)`, add:

```csharp
// Persist the server-assigned persistentIds between runs so already-processed notifications are not
// redelivered after a restart. Stored next to the config (gitignored). Ids have a server-side
// lifespan; a real app should prune this (e.g. cap the count) — kept simple here.
var persistentIdsPath = Path.Combine(AppContext.BaseDirectory, "persistent-ids.json");
var persistentIds = File.Exists(persistentIdsPath)
    ? JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(persistentIdsPath)) ?? []
    : new HashSet<string>();
Console.WriteLine($"Loaded {persistentIds.Count} persistent id(s).");
```

Change the construction to pass the set (keep the existing `loggerFactory` argument if present in the current file; if not, just add `persistentIds`):

```csharp
using var listener = new RustPlusFcm(credentials, persistentIds);
```

- [ ] **Step 2: Save incrementally as ids are harvested**

Among the other event subscriptions, add:

```csharp
listener.PersistentIdReceived += (_, _) =>
    File.WriteAllText(persistentIdsPath, JsonSerializer.Serialize(listener.PersistentIds));
```

- [ ] **Step 3: Build the sample**

Run: `dotnet build samples/RustPlus.Fcm.ConsoleApp/RustPlus.Fcm.ConsoleApp.csproj`
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add samples/RustPlus.Fcm.ConsoleApp/Program.cs
git commit -m "docs(sample): persist FCM persistentIds across runs to avoid duplicates"
```

---

### Task 6: Full verification, format, coverage

**Files:** none (verification only).

- [ ] **Step 1: Full solution build**

Run: `dotnet build RustPlusApi.sln`
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 2: Full test suite on both TFM hosts**

Run: `dotnet test RustPlusApi.sln`
Expected: every project PASS on net8.0 and net10.0, 0 failures, 0 skips.

- [ ] **Step 3: Coverage gate (new code must be 100/100; aggregate ≥ 95/90)**

Run: `tools/coverage/report.sh`
Expected: `RustPlusApi.Fcm.RustPlusFcmSocket` not regressed; the new `PersistentIds` getter and `PersistentIdReceived` raise path covered by the Task 1/2 tests; merged line ≥ 95%, branch ≥ 90%.
If `PersistentIds`'s `null` arm or the event's null-delegate arm shows uncovered, the Task 1 tests (`PersistentIds_NullCollection_SnapshotIsEmptyNotNull`, and the existing no-subscriber data-message tests) should already cover them — if not, add a no-subscriber harvest test.

- [ ] **Step 4: Format + member reorder**

Run: `dotnet tool restore && dotnet jb cleanupcode RustPlusApi.sln --profile="ReformatAndReorder"`
Then: `git diff --stat` — confirm only files this plan touched changed. Revert any unrelated reformatting (e.g. unrelated sample files) with `git checkout -- <path>`.

- [ ] **Step 5: Commit any formatting deltas**

```bash
git add -A
git commit -m "style(fcm): apply ReSharper formatting"
```

---

## Self-Review

**Spec coverage:**

- "Both" (event + property) → Task 1 (both members added to class + interface). ✓
- "Don't clear caller's set" → Task 2 (remove `Clear()`, invert the assertion test). ✓
- "Leave eviction to caller" → no eviction code; documented as caller's job in Task 1/4 docs. ✓
- Mutation-pinning of `Clear()` removed safely → Task 3 re-pins LoginResponse dispatch without `Clear()`. ✓
- Duplicate-window rationale (crash before save) → addressed by the incremental `PersistentIdReceived` event (Task 1) + sample incremental save (Task 5). ✓

**Placeholder scan:** No TBD/TODO; every code step shows complete code and exact commands. ✓

**Type consistency:** `PersistentIds` (`IReadOnlyCollection<string>`) and `PersistentIdReceived` (`EventHandler<string>`) are spelled identically in the class (Task 1 Step 3), interface (Task 1 Step 4), docs (Task 4), and sample (Task 5). The collection-expression snapshot `[.. persistentIds]` targets `IReadOnlyCollection<string>` and compiles on both TFMs. ✓

**Risk note:** The only nontrivial risk is Task 3 — if the new dispatch-proof test does not actually kill the same Stryker mutation the old one did, run `cd tests/RustPlusApi.Fcm.UnitTests && dotnet stryker --config-file stryker-config.json --project RustPlusApi.Fcm.csproj` and confirm the `OnMessageAsync`/first-frame-dispatch mutation is still killed; if not, add an explicit assertion on a non-faulting LoginResponse-only script.
