# FCM Notification Hierarchy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Unify every typed FCM event under a common notification envelope that carries `ServerId` + `PersistentId`, so consumers get consistent context (and can correlate an event to the harvested persistent id) across pairing AND alarm events.

**Architecture:** Introduce an abstract `NotificationBase` (`ServerId`, `PersistentId`). `Notification<T>` derives from it and adds pairing context (`PlayerId`, `PlayerToken`, `Data`). A new `AlarmNotification : NotificationBase` carries `Title`/`Message` (alarms have no player context — the server sends `PlayerId=0`/`PlayerToken=null`). `PersistentId` is threaded from `ParseNotification(FcmMessage)` down through the mappers (`ParsePairing` → `BuildGenericOutput`, and the alarm mapper). The `PersistentIds` snapshot + `PersistentIdReceived` event from the prior feature are retained (complementary: the event is the persistence hook; per-notification `PersistentId` is the correlation key).

**Tech Stack:** C# multi-targeting netstandard2.0 + net10.0, xUnit (run on net8.0 + net10.0 hosts), records with inheritance, source-generated logging.

## Global Constraints

- All `src/` libraries multi-target **netstandard2.0 + net10.0**; no net10-only API without a polyfill/`#if`. All new types use plain records/`Guid`/`string` (valid on both).
- Strict build: **TreatWarningsAsErrors** + Roslynator/Sonar/VSTHRD analyzers. Zero warnings.
- Tests pass on **BOTH** TFM hosts: `dotnet test RustPlusApi.sln`.
- Non-excluded FCM code expected at **100/100 line/branch**; new code fully covered. CI gate: line ≥ 95 / branch ≥ 90 (`tools/coverage/report.sh`).
- Do NOT bump versions in project files.
- Format before push: `dotnet jb cleanupcode RustPlusApi.sln --profile="ReformatAndReorder"`. Pre-push hook rejects unformatted outgoing files.
- This is a **BREAKING API change**: `OnAlarmTriggered`'s payload type changes from `AlarmEvent?` to `AlarmNotification?`; `Notification<T>` gains a base type and a `PersistentId` member. Pre-1.0 on `develop` — hard change, no deprecation shim.
- `PlayerToken` parsing: the existing `BuildGenericOutput` does `int.Parse(body.PlayerToken)`. This is only ever called for pairing (where `PlayerToken` is present), NEVER for alarms (null token) — preserve that invariant. `AlarmNotification` must NOT go through `BuildGenericOutput`.
- No push/PR without explicit user request (see [no-auto-commit] memory). Commits within the SDD task flow are authorized by choosing that execution mode.

---

## File Structure

| File | Responsibility | Change |
|---|---|---|
| `src/RustPlusApi.Fcm/Data/NotificationBase.cs` | The universal envelope (ServerId + PersistentId) | **Create** (abstract record) |
| `src/RustPlusApi.Fcm/Data/Notification.cs` | Pairing envelope (player + Data) | Modify: derive `NotificationBase`, drop duplicated `ServerId` |
| `src/RustPlusApi.Fcm/Data/Events/AlarmNotification.cs` | Alarm envelope (Title/Message, no player) | **Create** (`: NotificationBase`) |
| `src/RustPlusApi.Fcm/Data/Events/AlarmEvent.cs` | Old bare alarm record | **Delete** (replaced by `AlarmNotification`) |
| `src/RustPlusApi.Fcm/Utils/ResponseHelper.cs` | Builds `Notification<T>` from body | Modify: thread `persistentId` param |
| `src/RustPlusApi.Fcm/Extensions/MessageDataToEventModel.cs` | `ToAlarmEvent` mapper | Modify: → `ToAlarmNotification(serverId, persistentId)` |
| `src/RustPlusApi.Fcm/RustPlusFcm.cs` | Dispatch + mapper-threading | Modify: thread `persistentId` through `ParsePairing`/`ParsePairingEntity`; new alarm dispatch; `OnAlarmTriggered` type |
| `src/RustPlusApi.Fcm/Interfaces/IRustPlusFcm.cs` | Public event contract | Modify: `OnAlarmTriggered` payload type |
| `tests/RustPlusApi.Fcm.UnitTests/*` | Pin behavior | Modify: dispatch + mapper tests assert `PersistentId` + new alarm type |
| `samples/RustPlus.Fcm.ConsoleApp/Program.cs` | Sample | Modify: alarm handler uses `AlarmNotification` |
| `docs/articles/fcm-notifications.md` | User doc | Modify: event payload table + alarm shape |

---

### Task 1: Introduce `NotificationBase` and re-root `Notification<T>`

**Files:**
- Create: `src/RustPlusApi.Fcm/Data/NotificationBase.cs`
- Modify: `src/RustPlusApi.Fcm/Data/Notification.cs`
- Test: `tests/RustPlusApi.Fcm.UnitTests/` (add a small type-shape test file `NotificationHierarchyTests.cs`)

**Interfaces:**
- Produces:
  - `public abstract record NotificationBase { public Guid ServerId { get; init; } public string? PersistentId { get; init; } }`
  - `public record Notification<T> : NotificationBase { public ulong PlayerId { get; init; } public int PlayerToken { get; init; } public T? Data { get; init; } }` — note `ServerId` is now INHERITED (removed from `Notification<T>` itself).

- [ ] **Step 1: Write the failing test**

Create `tests/RustPlusApi.Fcm.UnitTests/NotificationHierarchyTests.cs`:

```csharp
using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Data.Events;
using Xunit;

namespace RustPlusApi.Fcm.UnitTests;

public class NotificationHierarchyTests
{
    [Fact]
    public void NotificationOfT_DerivesNotificationBase_AndCarriesServerIdAndPersistentId()
    {
        var serverId = Guid.NewGuid();
        NotificationBase n = new Notification<int?>
        {
            ServerId = serverId, PersistentId = "pid-1", PlayerId = 7, PlayerToken = 9, Data = 42
        };

        Assert.Equal(serverId, n.ServerId);
        Assert.Equal("pid-1", n.PersistentId);
        var typed = Assert.IsType<Notification<int?>>(n);
        Assert.Equal(7ul, typed.PlayerId);
        Assert.Equal(9, typed.PlayerToken);
        Assert.Equal(42, typed.Data);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/RustPlusApi.Fcm.UnitTests/RustPlusApi.Fcm.UnitTests.csproj -f net10.0 --filter "FullyQualifiedName~NotificationHierarchyTests"`
Expected: COMPILE ERROR — `NotificationBase` not defined; `PersistentId` not on `Notification`.

- [ ] **Step 3: Create `NotificationBase.cs`**

```csharp
namespace RustPlusApi.Fcm.Data;

/// <summary>The common envelope for every typed FCM notification: the originating server and the
/// FCM persistent id, so any event can be tied back to its server and de-duplicated/correlated with
/// the id harvested by the socket (see <c>PersistentIdReceived</c> / <c>PersistentIds</c>).</summary>
public abstract record NotificationBase
{
    /// <summary>The Rust+ server ID the notification originated from.</summary>
    public Guid ServerId { get; init; }

    /// <summary>The FCM persistent id of the underlying message; <see langword="null"/> if the
    /// message carried none. Use it to correlate this event with the harvested id set.</summary>
    public string? PersistentId { get; init; }
}
```

- [ ] **Step 4: Modify `Notification.cs` to derive the base**

Replace the whole record body so `ServerId` comes from the base and `PersistentId` is available:

```csharp
namespace RustPlusApi.Fcm.Data;

/// <summary>Wraps a typed FCM pairing payload with the originating player context, on top of the
/// shared <see cref="NotificationBase"/> server/persistent-id envelope.</summary>
/// <typeparam name="T">The pairing data type (e.g. <see cref="Events.EntityEvent"/>, <see cref="Events.ServerEvent"/>, or <see cref="ulong"/>).</typeparam>
public record Notification<T> : NotificationBase
{
    /// <summary>Steam ID of the player who performed the pairing.</summary>
    public ulong PlayerId { get; init; }

    /// <summary>Rust+ player token for the pairing player.</summary>
    public int PlayerToken { get; init; }

    /// <summary>The typed pairing payload.</summary>
    public T? Data { get; init; }
}
```

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test tests/RustPlusApi.Fcm.UnitTests/RustPlusApi.Fcm.UnitTests.csproj --filter "FullyQualifiedName~NotificationHierarchyTests"`
Expected: PASS on both TFMs. (Other tests may still build since `ServerId`/`Data` API on `Notification<T>` is unchanged; `BuildGenericOutput` still sets `ServerId` — now inherited — which compiles.)

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusApi.Fcm/Data/NotificationBase.cs src/RustPlusApi.Fcm/Data/Notification.cs tests/RustPlusApi.Fcm.UnitTests/NotificationHierarchyTests.cs
git commit -m "feat(fcm): add NotificationBase (ServerId + PersistentId), re-root Notification<T>"
```

---

### Task 2: Thread `PersistentId` into the pairing wrappers

**Files:**
- Modify: `src/RustPlusApi.Fcm/Utils/ResponseHelper.cs`
- Modify: `src/RustPlusApi.Fcm/RustPlusFcm.cs` (`ParseNotification`, `ParsePairing`, `ParsePairingEntity`)
- Test: `tests/RustPlusApi.Fcm.UnitTests/RustPlusFcmDispatchTests.cs`, `tests/RustPlusApi.Fcm.UnitTests/FcmResponseHelperTests.cs` (if present; else add to dispatch tests)

**Interfaces:**
- Consumes: `NotificationBase.PersistentId` from Task 1.
- Produces:
  - `BuildGenericOutput<T>(Body body, T data, string? persistentId)` — now sets `PersistentId = persistentId` on the returned `Notification<T?>`.
  - `RustPlusFcm.ParsePairing(Body body, string? persistentId)` and `ParsePairingEntity(Body body, string? persistentId)` — private, thread the id through.

- [ ] **Step 1: Write/extend the failing test**

In `RustPlusFcmDispatchTests.cs`, the existing `Pairing_Server_RaisesOnServerPairing` and `Pairing_Entity_RaisesTypedEntityEvents` tests build an `FcmMessage` via the `Pairing(Body)` helper. That helper currently sets only `Data = new MessageData { ChannelId="pairing", Body=body }`. Update the helper to also set a `PersistentId`, and add assertions. First, change the helper (near top of the test class):

```csharp
private static FcmMessage Pairing(Body body) =>
    new()
    {
        PersistentId = "pair-pid",
        Data = new MessageData
        {
            ChannelId = "pairing", Body = body
        }
    };
```

Then add to `Pairing_Server_RaisesOnServerPairing` (after the existing envelope asserts):

```csharp
        Assert.Equal("pair-pid", captured.PersistentId);
```

And to `Pairing_Entity_RaisesTypedEntityEvents` (after `entity` envelope asserts):

```csharp
        Assert.Equal("pair-pid", entity.PersistentId);
        Assert.Equal("pair-pid", raised.PersistentId);
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/RustPlusApi.Fcm.UnitTests/RustPlusApi.Fcm.UnitTests.csproj -f net10.0 --filter "FullyQualifiedName~Pairing_Server_RaisesOnServerPairing|FullyQualifiedName~Pairing_Entity_RaisesTypedEntityEvents"`
Expected: FAIL — `PersistentId` is null (not threaded yet), assertions on `"pair-pid"` fail.

- [ ] **Step 3: Thread the id through `ResponseHelper.cs`**

```csharp
    /// <summary>Wraps a typed pairing payload with the player/server context and persistent id from the notification.</summary>
    /// <typeparam name="T">The type of the pairing payload.</typeparam>
    /// <param name="body">The raw notification body providing player and server context.</param>
    /// <param name="data">The typed pairing payload to wrap.</param>
    /// <param name="persistentId">The FCM persistent id of the underlying message (may be <see langword="null"/>).</param>
    public static Notification<T?> BuildGenericOutput<T>(Body body, T data, string? persistentId)
    {
        return new Notification<T?>
        {
            PlayerId = body.PlayerId,
            PlayerToken = int.Parse(body.PlayerToken, CultureInfo.InvariantCulture),
            ServerId = body.Id,
            PersistentId = persistentId,
            Data = data
        };
    }
```

- [ ] **Step 4: Thread the id through `RustPlusFcm.cs` dispatch**

Change `ParseNotification`'s pairing arm to pass the id:

```csharp
            case "pairing":
                OnPairing?.Invoke(this, message);
                ParsePairing(message.Data.Body, message.PersistentId);
                break;
```

Update `ParsePairing` signature + the two `BuildGenericOutput` calls + the `ParsePairingEntity` call:

```csharp
    private void ParsePairing(Body body, string? persistentId)
    {
        switch (body.Type)
        {
            case "entity":
                var entity = BuildGenericOutput(body, body.ToEntityEvent(), persistentId);
                OnEntityPairing?.Invoke(this, entity);
                ParsePairingEntity(body, persistentId);
                break;
            case "server":
                var server = BuildGenericOutput(body, body.ToServerEvent(), persistentId);
                OnServerPairing?.Invoke(this, server);
                break;
            default:
                Logger.LogUnknownPairingType(body.Type);
                break;
        }
    }
```

Update `ParsePairingEntity`:

```csharp
    private void ParsePairingEntity(Body body, string? persistentId)
    {
        var response = BuildGenericOutput(body, body.ToEntityId(), persistentId);

        switch (body.EntityType)
        {
            case 1:
                OnSmartSwitchPairing?.Invoke(this, response);
                break;
            case 2:
                OnSmartAlarmPairing?.Invoke(this, response);
                break;
            case 3:
                OnStorageMonitorPairing?.Invoke(this, response);
                break;
            default:
                Logger.LogUnknownEntityType(body.EntityType);
                break;
        }
    }
```

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test tests/RustPlusApi.Fcm.UnitTests/RustPlusApi.Fcm.UnitTests.csproj --filter "FullyQualifiedName~Pairing_Server_RaisesOnServerPairing|FullyQualifiedName~Pairing_Entity_RaisesTypedEntityEvents"`
Expected: PASS both TFMs.

- [ ] **Step 6: Fix any other callers of `BuildGenericOutput`**

Run: `grep -rn "BuildGenericOutput" src tests --include=*.cs | grep -v /obj/ | grep -v /bin/`
For each call NOT already updated, add the `persistentId` argument (tests calling it directly should pass an explicit value or `null`). Re-run the full FCM unit project: `dotnet test tests/RustPlusApi.Fcm.UnitTests/RustPlusApi.Fcm.UnitTests.csproj` — expected all green except the alarm tests if they reference the not-yet-changed alarm path (the alarm path is untouched in this task, so they should still pass).

- [ ] **Step 7: Commit**

```bash
git add src/RustPlusApi.Fcm/Utils/ResponseHelper.cs src/RustPlusApi.Fcm/RustPlusFcm.cs tests/RustPlusApi.Fcm.UnitTests/RustPlusFcmDispatchTests.cs
git commit -m "feat(fcm): thread PersistentId into pairing notification wrappers"
```

---

### Task 3: Replace `AlarmEvent` with `AlarmNotification : NotificationBase`

**Files:**
- Create: `src/RustPlusApi.Fcm/Data/Events/AlarmNotification.cs`
- Delete: `src/RustPlusApi.Fcm/Data/Events/AlarmEvent.cs`
- Modify: `src/RustPlusApi.Fcm/Extensions/MessageDataToEventModel.cs`
- Modify: `src/RustPlusApi.Fcm/RustPlusFcm.cs` (alarm dispatch + `OnAlarmTriggered` event type)
- Modify: `src/RustPlusApi.Fcm/Interfaces/IRustPlusFcm.cs`
- Test: `tests/RustPlusApi.Fcm.UnitTests/FcmExtensionMapperTests.cs`, `tests/RustPlusApi.Fcm.UnitTests/RustPlusFcmDispatchTests.cs`

**Interfaces:**
- Consumes: `NotificationBase` (Task 1).
- Produces:
  - `public sealed record AlarmNotification : NotificationBase { public string Title { get; init; } = null!; public string Message { get; init; } = null!; }`
  - `MessageData.ToAlarmNotification(Guid serverId, string? persistentId)` → `AlarmNotification`.
  - `OnAlarmTriggered` event type becomes `EventHandler<AlarmNotification?>?` on both class and interface.

- [ ] **Step 1: Rewrite the mapper test**

In `FcmExtensionMapperTests.cs`, replace the `ToAlarmEvent_MapsServerIdTitleAndMessage` test with:

```csharp
    [Fact]
    public void ToAlarmNotification_MapsServerIdPersistentIdTitleAndMessage()
    {
        var serverId = Guid.Parse("52d121e8-9d14-4dc5-928a-84aa531cfc9e");
        var data = new MessageData
        {
            Title = "Base attacked",
            Message = "Door opened",
            Body = new Body { Id = serverId }
        };
        var ev = data.ToAlarmNotification(serverId, "alarm-pid");
        Assert.Equal(serverId, ev.ServerId);
        Assert.Equal("alarm-pid", ev.PersistentId);
        Assert.Equal("Base attacked", ev.Title);
        Assert.Equal("Door opened", ev.Message);
    }
```

- [ ] **Step 2: Rewrite the dispatch test**

In `RustPlusFcmDispatchTests.cs`, replace `Alarm_RaisesOnAlarmTriggered`'s body so it captures `AlarmNotification?`, sets a `PersistentId` on the message, and asserts it:

```csharp
    [Fact]
    public void Alarm_RaisesOnAlarmTriggered()
    {
        using var fcm = new TestFcm();
        var serverId = Guid.Parse("52d121e8-9d14-4dc5-928a-84aa531cfc9e");
        AlarmNotification? captured = null;
        fcm.OnAlarmTriggered += (_, e) => captured = e;

        fcm.Feed(new FcmMessage
        {
            PersistentId = "alarm-pid",
            Data = new MessageData
            {
                ChannelId = "alarm",
                Title = "the title",
                Message = "the message",
                Body = new Body { Id = serverId }
            }
        });

        Assert.NotNull(captured);
        Assert.Equal(serverId, captured!.ServerId);
        Assert.Equal("alarm-pid", captured.PersistentId);
        Assert.Equal("the title", captured.Title);
        Assert.Equal("the message", captured.Message);
    }
```

Also update any other alarm test in this file that declares `AlarmEvent? captured` (e.g. `Alarm_NoHandler_DoesNotRaiseOtherEvents` only subscribes nothing — leave it; but search for `AlarmEvent` and fix the type names).

- [ ] **Step 3: Run to verify they fail**

Run: `dotnet test tests/RustPlusApi.Fcm.UnitTests/RustPlusApi.Fcm.UnitTests.csproj -f net10.0 --filter "FullyQualifiedName~ToAlarmNotification|FullyQualifiedName~Alarm_RaisesOnAlarmTriggered"`
Expected: COMPILE ERROR — `AlarmNotification` / `ToAlarmNotification` not defined.

- [ ] **Step 4: Create `AlarmNotification.cs`, delete `AlarmEvent.cs`**

Create `src/RustPlusApi.Fcm/Data/Events/AlarmNotification.cs`:

```csharp
using RustPlusApi.Fcm.Data;

namespace RustPlusApi.Fcm.Data.Events;

/// <summary>A triggered Rust+ smart alarm, on top of the shared <see cref="NotificationBase"/>
/// server/persistent-id envelope. Alarms carry no player context (the server sends none).</summary>
public sealed record AlarmNotification : NotificationBase
{
    /// <summary>The alarm title configured in the Rust+ app.</summary>
    public string Title { get; init; } = null!;

    /// <summary>The alarm message configured in the Rust+ app.</summary>
    public string Message { get; init; } = null!;
}
```

Delete `src/RustPlusApi.Fcm/Data/Events/AlarmEvent.cs`:

```bash
git rm src/RustPlusApi.Fcm/Data/Events/AlarmEvent.cs
```

- [ ] **Step 5: Rewrite the mapper `MessageDataToEventModel.cs`**

```csharp
using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Data.Events;

namespace RustPlusApi.Fcm.Extensions;

/// <summary>Extension methods that project a <see cref="MessageData"/> into FCM event model types.</summary>
public static class MessageDataToEventModel
{
    /// <summary>Maps the notification message data to an <see cref="AlarmNotification"/>.</summary>
    /// <param name="data">The message data to map.</param>
    /// <param name="serverId">The ID of the server the alarm was triggered on.</param>
    /// <param name="persistentId">The FCM persistent id of the alarm message (may be <see langword="null"/>).</param>
    public static AlarmNotification ToAlarmNotification(this MessageData data, Guid serverId, string? persistentId)
    {
        return new AlarmNotification
        {
            ServerId = serverId, PersistentId = persistentId, Title = data.Title, Message = data.Message
        };
    }
}
```

- [ ] **Step 6: Update dispatch + event type in `RustPlusFcm.cs`**

Change the alarm arm:

```csharp
            case "alarm":
                OnAlarmTriggered?.Invoke(this, message.Data.ToAlarmNotification(message.Data.Body.Id, message.PersistentId));
                break;
```

Change the event declaration:

```csharp
    /// <summary>
    /// Occurs when an alarm event is triggered.
    /// </summary>
    /// <remarks>
    /// The event data is an <see cref="AlarmNotification"/> (server id + persistent id + title/message).
    /// </remarks>
    public event EventHandler<AlarmNotification?>? OnAlarmTriggered;
```

- [ ] **Step 7: Update the interface `IRustPlusFcm.cs`**

```csharp
    /// <summary>Raised when a smart alarm is triggered. Payload is an <see cref="AlarmNotification"/>.</summary>
    event EventHandler<AlarmNotification?>? OnAlarmTriggered;
```

- [ ] **Step 8: Run to verify they pass + full FCM project green**

Run: `dotnet test tests/RustPlusApi.Fcm.UnitTests/RustPlusApi.Fcm.UnitTests.csproj`
Expected: all PASS on both TFMs. If any test still references `AlarmEvent` or `ToAlarmEvent`, fix the type/method name (search: `grep -rn "AlarmEvent\b\|ToAlarmEvent" tests src --include=*.cs | grep -v /obj/ | grep -v /bin/` — must return nothing in active code).

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat(fcm)!: replace AlarmEvent with AlarmNotification : NotificationBase"
```

---

### Task 4: Update sample + DI + docs

**Files:**
- Modify: `samples/RustPlus.Fcm.ConsoleApp/Program.cs`
- Modify: `docs/articles/fcm-notifications.md`
- Check: `src/RustPlusApi.Fcm.Extensions.DependencyInjection/` (does anything reference `AlarmEvent`?)

**Interfaces:**
- Consumes: `AlarmNotification`, `NotificationBase.PersistentId` (Tasks 1-3).
- Produces: docs/sample only.

- [ ] **Step 1: Find and fix all remaining `AlarmEvent` references outside core**

Run: `grep -rn "AlarmEvent\b" samples docs src/RustPlusApi.Fcm.Extensions.DependencyInjection --include=*.cs --include=*.md | grep -v /obj/ | grep -v /bin/`
For the sample `Program.cs` alarm handler (`listener.OnAlarmTriggered += (_, alarm) => ...`), the lambda's `alarm` is now `AlarmNotification?`. If it serializes `alarm` via `JsonSerializer.Serialize(alarm, ...)`, no change needed (still serializes; now includes ServerId + PersistentId). If it accesses `.Title`/`.Message`, those still exist. Only adjust if a member was removed (none were). Confirm it compiles.

- [ ] **Step 2: Update the docs event table**

In `docs/articles/fcm-notifications.md`, the Events table row for the alarm event must change its payload type from `AlarmEvent` to `AlarmNotification`, and the pairing rows should note they now also carry `PersistentId` (via `NotificationBase`). Update the prose to mention that every typed event derives `NotificationBase` (ServerId + PersistentId) so events can be correlated with the harvested persistent id. Keep edits tight and in the article's voice.

- [ ] **Step 3: Build sample + confirm docs crefs**

Run: `dotnet build samples/RustPlus.Fcm.ConsoleApp/RustPlus.Fcm.ConsoleApp.csproj src/RustPlusApi.Fcm/RustPlusApi.Fcm.csproj`
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add samples/RustPlus.Fcm.ConsoleApp/Program.cs docs/articles/fcm-notifications.md
git commit -m "docs(fcm): document NotificationBase hierarchy + AlarmNotification"
```

---

### Task 5: Full verification, format, coverage

**Files:** none (verification only).

- [ ] **Step 1: Full solution build** — `dotnet build RustPlusApi.sln` → 0 warnings / 0 errors.
- [ ] **Step 2: Full test suite both TFMs** — `dotnet test RustPlusApi.sln` → every project PASS on net8.0 + net10.0, 0 failures, 0 skips.
- [ ] **Step 3: Coverage gate** — `tools/coverage/report.sh` → merged line ≥ 95 / branch ≥ 90; `NotificationBase`, `AlarmNotification`, `Notification`, `ResponseHelper`, `MessageDataToEventModel` covered. New records are pure data; ensure every new member is touched by a test (Tasks 1-3 assert them). If a coverage gap appears on `NotificationBase`/`AlarmNotification`, it means a member is unread by tests — add an assertion.
- [ ] **Step 4: Format** — `dotnet tool restore && dotnet jb cleanupcode RustPlusApi.sln --profile="ReformatAndReorder"`, then `git diff --stat` and revert any unrelated reformatting (`git checkout -- <path>`; the Camera samples have known pre-existing drift — do NOT include them).
- [ ] **Step 5: Commit formatting** — `git add -A && git commit -m "style(fcm): apply ReSharper formatting"` (only if the formatter changed THIS feature's files).

---

## Self-Review

**Spec coverage:**
- Base notification with `ServerId` + `PersistentId` → Task 1 (`NotificationBase`). ✓
- Extended notification with player/entity context → Task 1 (`Notification<T> : NotificationBase`). ✓
- `AlarmEvent` unified → Task 3 (`AlarmNotification : NotificationBase`, dedicated type, no player fields). ✓
- PersistentId threaded so each event carries it → Task 2 (pairing) + Task 3 (alarm). ✓
- `PersistentIds`/`PersistentIdReceived` retained → untouched (not in any task's modify list). ✓
- Breaking event-type change surfaced → Task 3 (event type on class + interface). ✓
- Sample + docs → Task 4. ✓

**Placeholder scan:** All steps show concrete code/commands. The only "find and fix" steps (Task 2 Step 6, Task 4 Step 1) are bounded grep-then-edit with the exact change described (add `persistentId` arg / rename type) — acceptable because the edit is mechanical and identical per hit. ✓

**Type consistency:** `NotificationBase` (ServerId: `Guid`, PersistentId: `string?`); `Notification<T> : NotificationBase` (PlayerId `ulong`, PlayerToken `int`, Data `T?`); `AlarmNotification : NotificationBase` (Title/Message `string`); `BuildGenericOutput<T>(Body, T, string?)`; `ToAlarmNotification(this MessageData, Guid, string?)`; `OnAlarmTriggered` → `EventHandler<AlarmNotification?>?`. All names/types are spelled identically across tasks. ✓

**Risk note:** The `PlayerToken` `int.Parse` invariant — `AlarmNotification` deliberately does NOT go through `BuildGenericOutput`, so the null-token parse can never hit an alarm. Confirmed: Task 3's alarm mapper builds `AlarmNotification` directly. The only behavioral risk is a missed `AlarmEvent`/`ToAlarmEvent`/`BuildGenericOutput` caller — Tasks 2 Step 6, 3 Step 8, and 4 Step 1 each have an explicit grep gate that must return clean.
