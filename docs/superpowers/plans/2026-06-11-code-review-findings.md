# CODE_REVIEW.md Findings — Resolution Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Resolve every still-relevant finding from `CODE_REVIEW.md` (review of `a476edd`, 2026-06-11), accounting for commit `4145347` (#64) which landed after the review.

**Architecture:** All changes are localized hardening/API-additive fixes inside the existing two-layer socket design — no structural changes. Core socket fixes (M1–M3, M6, L1, L2, L3) in `src/RustPlusApi`, FCM fixes (L4, L5, L6, and options clone) in `src/RustPlusApi.Fcm`, registration fixes (M5, L7, L8) in `src/RustPlusApi.Fcm.Registration`.

**Tech Stack:** C# multi-targeting `netstandard2.0; net10.0`, xUnit (offline `MockRustPlusServer` for integration tests, `StubHttpMessageHandler` for HTTP steps, `RunReceiveLoopOverStreamAsync` seam for MCS framing). CI gates: ≥95% line / ≥90% branch coverage, `TreatWarningsAsErrors` with `latest-all` analyzers — **every new public member needs XML docs, every new catch block needs test coverage.**

---

## Finding status after commit 4145347 (#64)

| Finding | Status | Plan |
| ------- | ------ | ---- |
| M1 send loop faults silently | **Open** | Task 1 |
| M2 send-while-disconnected queues silently | **Open** | Task 2 |
| M3 `ConnectAsync` concurrency | **Open** | Task 3 |
| M4 FCM receive loop swallows faults | **Already fixed** by #64 — generic catch raising `ErrorOccurred` now at `RustPlusFcmSocket.cs:492-504` | none |
| M5 FCM token parsing brittle | **Open** (#64 touched `CheckInAsync`, not `RegisterFcmAsync`) | Task 5 |
| M6 no machine-readable error code | **Open** | Task 4 |
| L1 manual options snapshot | **Open** | Task 8 |
| L2 `RequestSent` fires on enqueue | **Open** | Task 11 (docs fix) |
| L3 switch-vs-alarm heuristic undocumented | **Open** | Task 11 (docs fix) |
| L4 `_lastTraffic` unsynchronized | **Open** | Task 6 |
| L5 `persistentIds` growth/O(n) | **Open** | Task 11 (docs fix) |
| L6 `bodyData!` on attacker-influenced input | **Open** | Task 7 |
| L7 `SteamLoginService` cancellation | **Open** | Task 9 |
| L8 plaintext credentials, default perms | **Open** | Task 10 |
| L9 camera renderer JS naming | **Deferred** — the review's own precondition (a golden-frame test) is still unmet: `CameraRendererTests.cs:21` says fidelity validation is pending. The #64 sample-offset fix was validated live but no captured frame is in the repo. Revisit when one lands. | none |
| §6 CI ignores `main` | **Process note, not code** — verify in repo settings that `main` requires the CD-side checks before merge. Not a task here. | none |

**Execution notes:**

- Run tests per task as shown; run the **full** suite + build at the end (Task 12).
- Tasks 1→2 must run in order (Task 1's test uses a seam precisely because Task 2 closes the public path). Other tasks are independent.
- Repo convention: conventional-commit prefixes (`fix:`, `feat:`, `docs:`, `test:`). The user's standing preference is **no commits unless asked** — the commit steps below are authorized by approving this plan, but confirm before pushing anything.

---

### Task 1: M1 — Send loop must surface unexpected faults

During a reconnect, `ConnectAsync` disposes and nulls `_webSocket` while the send loop may still be draining; the loop's `_webSocket!.SendAsync` then throws `NullReferenceException`/`ObjectDisposedException`, which no catch handles — the loop dies silently and pending requests wait out their full 30 s timeout.

**Files:**

- Modify: `src/RustPlusApi/RustPlusSocket.cs` (`ProcessSendQueueAsync`, ~line 550; test-seam block ~line 134)
- Test: `tests/RustPlusApi.IntegrationTests/SocketErrorTests.cs`

- [ ] **Step 1: Add the test seam**

In `src/RustPlusApi/RustPlusSocket.cs`, next to the existing seams (after `PendingRequestCountForTests`, ~line 135), add:

```csharp
    /// <summary>Test seam: enqueues a request directly onto the send channel, bypassing
    /// <see cref="SendRequestAsync"/>'s state checks, to exercise the send loop's fault path.</summary>
    internal void EnqueueRequestForTests(AppRequest request) => _sendChannel.Writer.TryWrite(request);
```

- [ ] **Step 2: Write the failing test**

In `tests/RustPlusApi.IntegrationTests/SocketErrorTests.cs` (match the file's existing `PlayerId`/`PlayerToken`/`Timeout` constants — add them if that file doesn't have them, copying from `SocketCorrelationTests.cs`):

```csharp
    [Fact]
    public async Task SendLoop_NonWebSocketFault_RaisesErrorOccurredAndExits()
    {
        // A failed reconnect leaves _webSocket null while the send loop (started by the first
        // connect) is still draining the channel. A request entering the channel in that window
        // must surface on ErrorOccurred and exit the loop cleanly — not kill it silently.
        var server = new MockRustPlusServer(MockResponses.Default);
        server.Start();
        await using var client =
            new RustPlus(new RustPlusConnection(MockRustPlusServer.Host, server.Port, PlayerId, PlayerToken));

        await client.ConnectAsync().WaitAsync(Timeout);
        await client.DisconnectAsync();
        await server.DisposeAsync(); // free the endpoint so the reconnect below is refused

        // The reconnect disposes/nulls the old socket, then fails; the send loop stays alive.
        await Assert.ThrowsAnyAsync<Exception>(() => client.ConnectAsync().WaitAsync(Timeout));

        // Subscribe only now, so the connect failure above cannot satisfy the assertion.
        var errorTcs = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.ErrorOccurred += (_, ex) => errorTcs.TrySetResult(ex);

        client.EnqueueRequestForTests(new AppRequest
        {
            GetInfo = new AppEmpty()
        });

        var observed = await errorTcs.Task.WaitAsync(Timeout);
        Assert.True(observed is NullReferenceException or ObjectDisposedException,
            $"Unexpected fault type: {observed.GetType()}");
        // The loop exited through the fault handler instead of dying mid-iteration unobserved.
        await client.SendLoopForTests!.WaitAsync(Timeout);
    }
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test tests/RustPlusApi.IntegrationTests --filter "FullyQualifiedName~SendLoop_NonWebSocketFault"`
Expected: FAIL — `errorTcs.Task.WaitAsync(Timeout)` times out, because the NRE propagates out of the loop task unobserved.

- [ ] **Step 4: Add the catch-all to `ProcessSendQueueAsync`**

In `src/RustPlusApi/RustPlusSocket.cs`, after the existing `catch (WebSocketException ex)` block (~line 579), add:

```csharp
        catch (Exception ex)
        {
            // A concurrent reconnect can dispose/null _webSocket while this loop is draining
            // (NullReferenceException / ObjectDisposedException). Left uncaught, the loop dies
            // invisibly and every pending request waits out its full timeout — surface the fault
            // and fail them now instead. ConnectAsync restarts the loop on the next connect.
            Logger.LogSendLoopFaulted(ex);
            ErrorOccurred?.Invoke(this, ex);
            FailPendingRequests(ex);
        }
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/RustPlusApi.IntegrationTests --filter "FullyQualifiedName~SendLoop_NonWebSocketFault"`
Expected: PASS

- [ ] **Step 6: Run the whole integration project (regression check on lifecycle/teardown suites)**

Run: `dotnet test tests/RustPlusApi.IntegrationTests`
Expected: all PASS

- [ ] **Step 7: Commit**

```bash
git add src/RustPlusApi/RustPlusSocket.cs tests/RustPlusApi.IntegrationTests/SocketErrorTests.cs
git commit -m "fix: surface unexpected send-loop faults via ErrorOccurred (M1)"
```

---

### Task 2: M2 — Fail fast when sending while disconnected

`SendRequestAsync` unconditionally enqueues; called while disconnected the caller gets a 30 s `TimeoutException`, and the stale request is then transmitted on the next reconnect, out of context.

**Files:**

- Modify: `src/RustPlusApi/RustPlusSocket.cs:277-302` (`SendRequestAsync`)
- Test: `tests/RustPlusApi.IntegrationTests/SocketErrorTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
    [Fact]
    public async Task SendRequestAsync_NeverConnected_ThrowsInvalidOperationException()
    {
        // Fail fast with a clear error instead of queueing into a 30s TimeoutException
        // (and instead of transmitting the stale request on a later reconnect).
        await using var client =
            new RustPlus(new RustPlusConnection(MockRustPlusServer.Host, 1, PlayerId, PlayerToken));

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetInfoAsync());
    }

    [Fact]
    public async Task SendRequestAsync_AfterDisconnect_ThrowsInvalidOperationException()
    {
        await using var server = new MockRustPlusServer(MockResponses.Default);
        server.Start();
        await using var client =
            new RustPlus(new RustPlusConnection(MockRustPlusServer.Host, server.Port, PlayerId, PlayerToken));
        await client.ConnectAsync().WaitAsync(Timeout);
        await client.DisconnectAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetInfoAsync());
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/RustPlusApi.IntegrationTests --filter "FullyQualifiedName~ThrowsInvalidOperationException"`
Expected: FAIL — currently the request queues and the calls time out (the never-connected test will take ~30 s; that's the bug).

- [ ] **Step 3: Add the guard**

In `SendRequestAsync` (`src/RustPlusApi/RustPlusSocket.cs`), insert at the very top of the method body (before the `tcs` creation, ~line 281):

```csharp
        if (!IsConnected)
        {
            // Queueing here would mean a generic 30s timeout now and a stale, out-of-context
            // request transmitted on the next reconnect — fail fast instead.
            throw new InvalidOperationException("Not connected. Call ConnectAsync before sending requests.");
        }
```

And add to the method's XML docs (next to the existing `<exception cref="TimeoutException">`):

```csharp
    /// <exception cref="InvalidOperationException">Thrown when the client is not connected.</exception>
```

If `IRustPlusSocket` declares `SendRequestAsync` with its own XML docs, mirror the `<exception>` tag there.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/RustPlusApi.IntegrationTests --filter "FullyQualifiedName~ThrowsInvalidOperationException"`
Expected: PASS (and fast — no 30 s waits)

- [ ] **Step 5: Run the full integration + unit projects**

Run: `dotnet test tests/RustPlusApi.IntegrationTests && dotnet test tests/RustPlusApi.UnitTests`
Expected: all PASS. If any existing test intentionally sent while disconnected (none was found during planning), update it to expect `InvalidOperationException` — that is the new, documented contract.

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusApi/RustPlusSocket.cs src/RustPlusApi/Interfaces tests/RustPlusApi.IntegrationTests/SocketErrorTests.cs
git commit -m "fix: fail fast on send-while-disconnected instead of queue-and-timeout (M2)"
```

---

### Task 3: M3 — Serialize connect/disconnect transitions

Two concurrent `ConnectAsync` calls can both pass the `IsConnected` check and race on `_webSocket` (one connection leaks).

**Files:**

- Modify: `src/RustPlusApi/RustPlusSocket.cs` (`ConnectAsync` ~163, `DisconnectAsync` ~344, `Dispose(bool)` ~396, `DisposeCoreAsync` ~428)
- Test: `tests/RustPlusApi.IntegrationTests/SocketLifecycleTests.cs`

- [ ] **Step 1: Write the test**

In `tests/RustPlusApi.IntegrationTests/SocketLifecycleTests.cs` (reuse that file's constants):

```csharp
    [Fact]
    public async Task ConnectAsync_ConcurrentCalls_ExactlyOneSucceeds()
    {
        // Lifecycle transitions are serialized: of two racing connects, one wins and the other
        // observes the connected state and throws — neither leaks a socket nor starts a second
        // receive loop.
        await using var server = new MockRustPlusServer(MockResponses.Default);
        server.Start();
        await using var client =
            new RustPlus(new RustPlusConnection(MockRustPlusServer.Host, server.Port, PlayerId, PlayerToken));

        var outcomes = await Task.WhenAll(
            ObserveAsync(client.ConnectAsync()),
            ObserveAsync(client.ConnectAsync()));

        Assert.True(client.IsConnected);
        Assert.Equal(1, outcomes.Count(static e => e is null));
        Assert.Equal(1, outcomes.Count(static e => e is InvalidOperationException));
    }

    /// <summary>Awaits <paramref name="task"/> and returns its exception instead of throwing.</summary>
    /// <param name="task">The task whose outcome to observe.</param>
    private static async Task<Exception?> ObserveAsync(Task task)
    {
        try
        {
            await task;
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
```

- [ ] **Step 2: Run it to observe current behavior**

Run: `dotnet test tests/RustPlusApi.IntegrationTests --filter "FullyQualifiedName~ConcurrentCalls_ExactlyOneSucceeds"`
Expected: FLAKY before the fix (it's a race — both calls may succeed, or it may pass by luck). Run it a few times; any run with two successes demonstrates the bug. The fix makes the outcome deterministic.

- [ ] **Step 3: Add the lifecycle lock**

In `src/RustPlusApi/RustPlusSocket.cs`, next to `_cancellationTokenSource` (~line 137):

```csharp
    /// <summary>Serializes connect/disconnect transitions so concurrent lifecycle calls cannot race
    /// on <see cref="_webSocket"/> (leaking a connection or briefly running two receive loops).</summary>
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
```

Wrap `ConnectAsync`'s body **after** the `ObjectDisposedException` check (so disposal still throws without touching the lock):

```csharp
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // ... entire existing body from `if (IsConnected)` through `Connected?.Invoke(...)` ...
        }
        finally
        {
            _lifecycleLock.Release();
        }
```

Wrap `DisconnectAsync`'s body the same way (it has no caller token; use the parameterless wait):

```csharp
        await _lifecycleLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // ... entire existing body from `if (!IsConnected)` through `Disconnected?.Invoke(...)` ...
        }
        finally
        {
            _lifecycleLock.Release();
        }
```

Dispose the semaphore in **both** teardown paths, after the loops are done (mirroring how `_cancellationTokenSource` is handled): add `_lifecycleLock.Dispose();` as the last line of `Dispose(bool)` (~line 410) and of `DisposeCoreAsync` (~line 444).

Note: disposal cancels the instance token first, which unwinds any in-flight `ConnectAsync` before the semaphore is disposed. If a teardown test surfaces `ObjectDisposedException` from `_lifecycleLock.Release()` in the finally, wrap that specific `Release()` in `try { } catch (ObjectDisposedException) { /* torn down mid-transition */ }` — only if a test actually demands it (YAGNI otherwise).

- [ ] **Step 4: Run the new test repeatedly to verify determinism**

Run: `for i in 1 2 3 4 5; do dotnet test tests/RustPlusApi.IntegrationTests --filter "FullyQualifiedName~ConcurrentCalls_ExactlyOneSucceeds" || break; done`
Expected: PASS ×5

- [ ] **Step 5: Run lifecycle + teardown suites (the lock must not deadlock disposal)**

Run: `dotnet test tests/RustPlusApi.IntegrationTests`
Expected: all PASS

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusApi/RustPlusSocket.cs tests/RustPlusApi.IntegrationTests/SocketLifecycleTests.cs
git commit -m "fix: serialize connect/disconnect transitions with a lifecycle lock (M3)"
```

---

### Task 4: M6 — Machine-readable error codes on responses

`ErrorMessage` exposes only free text; consumers must string-compare server identifiers like `"not_found"`. Add an additive, binary-compatible `Code` enum.

**Files:**

- Create: `src/RustPlusApi/Data/RustPlusErrorCode.cs`
- Modify: `src/RustPlusApi/Data/Response.cs` (add `Code` to `ErrorMessage`)
- Modify: `src/RustPlusApi/Utils/ResponseHelper.cs` (parse + set `Code`)
- Test: `tests/RustPlusApi.UnitTests/ErrorCodeTests.cs` (new)

- [ ] **Step 1: Write the failing tests**

Create `tests/RustPlusApi.UnitTests/ErrorCodeTests.cs`:

```csharp
using RustPlusApi.Data;
using RustPlusApi.Utils;
using Xunit;

namespace RustPlusApi.UnitTests;

/// <summary>The raw server error identifier is surfaced as a machine-readable
/// <see cref="RustPlusErrorCode"/> alongside the untouched <see cref="ErrorMessage.Message"/>.</summary>
public class ErrorCodeTests
{
    [Theory]
    [InlineData("server_error", RustPlusErrorCode.ServerError)]
    [InlineData("banned", RustPlusErrorCode.Banned)]
    [InlineData("rate_limit", RustPlusErrorCode.RateLimit)]
    [InlineData("not_found", RustPlusErrorCode.NotFound)]
    [InlineData("wrong_type", RustPlusErrorCode.WrongType)]
    [InlineData("no_team", RustPlusErrorCode.NoTeam)]
    [InlineData("no_clan", RustPlusErrorCode.NoClan)]
    [InlineData("no_map", RustPlusErrorCode.NoMap)]
    [InlineData("access_denied", RustPlusErrorCode.AccessDenied)]
    [InlineData("message_not_sent", RustPlusErrorCode.MessageNotSent)]
    [InlineData("too_many_subscribers", RustPlusErrorCode.TooManySubscribers)]
    [InlineData("not_enabled", RustPlusErrorCode.NotEnabled)]
    [InlineData("unknown-error", RustPlusErrorCode.Unknown)]
    [InlineData("some_future_identifier", RustPlusErrorCode.Unknown)]
    public void BuildAckOutput_MapsServerIdentifierToCode(string identifier, RustPlusErrorCode expected)
    {
        var response = ResponseHelper.BuildAckOutput(false, identifier);

        Assert.NotNull(response.Error);
        Assert.Equal(expected, response.Error!.Code);
        Assert.Equal(identifier, response.Error.Message); // the raw string stays available
    }

    [Fact]
    public void BuildGenericOutput_MapsServerIdentifierToCode()
    {
        var response = ResponseHelper.BuildGenericOutput<string>(false, default!, "not_found");

        Assert.Equal(RustPlusErrorCode.NotFound, response.Error!.Code);
    }

    [Fact]
    public void BuildAckOutput_Success_HasNoError()
    {
        Assert.Null(ResponseHelper.BuildAckOutput(true).Error);
    }
}
```

- [ ] **Step 2: Run to verify they fail to compile**

Run: `dotnet test tests/RustPlusApi.UnitTests --filter "FullyQualifiedName~ErrorCodeTests"`
Expected: BUILD FAILURE — `RustPlusErrorCode` does not exist.

- [ ] **Step 3: Create the enum**

Create `src/RustPlusApi/Data/RustPlusErrorCode.cs`. The identifiers are the well-known Rust+ server error strings as documented by the rustplus.js community wiki; anything else maps to `Unknown`, so a wrong or missing entry can never break a consumer — only under-classify.

```csharp
namespace RustPlusApi.Data;

/// <summary>
/// Machine-readable Rust+ server error identifiers, parsed from the raw error string so consumers
/// can branch on failure type without string comparisons. The raw identifier always remains
/// available on <see cref="ErrorMessage.Message"/>; unrecognized identifiers map to <see cref="Unknown"/>.
/// </summary>
public enum RustPlusErrorCode
{
    /// <summary>The server returned an identifier this library does not recognize (or none at all);
    /// inspect <see cref="ErrorMessage.Message"/> for the raw value.</summary>
    Unknown = 0,

    /// <summary><c>server_error</c> — the server failed to process the request.</summary>
    ServerError,

    /// <summary><c>banned</c> — the player is banned from the server.</summary>
    Banned,

    /// <summary><c>rate_limit</c> — the request was throttled; retry later.</summary>
    RateLimit,

    /// <summary><c>not_found</c> — the requested entity or resource does not exist.</summary>
    NotFound,

    /// <summary><c>wrong_type</c> — the entity exists but is not of the requested kind.</summary>
    WrongType,

    /// <summary><c>no_team</c> — the player is not in a team.</summary>
    NoTeam,

    /// <summary><c>no_clan</c> — the player is not in a clan.</summary>
    NoClan,

    /// <summary><c>no_map</c> — the server has no map image available.</summary>
    NoMap,

    /// <summary><c>access_denied</c> — the player token does not grant access to this resource.</summary>
    AccessDenied,

    /// <summary><c>message_not_sent</c> — the team chat message was rejected.</summary>
    MessageNotSent,

    /// <summary><c>too_many_subscribers</c> — the entity has reached its subscription limit.</summary>
    TooManySubscribers,

    /// <summary><c>not_enabled</c> — the requested feature is disabled on this server.</summary>
    NotEnabled,
}
```

- [ ] **Step 4: Add `Code` to `ErrorMessage`**

In `src/RustPlusApi/Data/Response.cs`, extend the `ErrorMessage` record:

```csharp
/// <summary>Error detail attached to a failed <see cref="Response{T}"/>.</summary>
public sealed record ErrorMessage
{
    /// <summary>Human-readable description of the error returned by the server.</summary>
    public string? Message { get; init; }

    /// <summary>Machine-readable identifier parsed from <see cref="Message"/>;
    /// <see cref="RustPlusErrorCode.Unknown"/> when the identifier is not recognized.</summary>
    public RustPlusErrorCode Code { get; init; }
}
```

- [ ] **Step 5: Parse the code in `ResponseHelper`**

In `src/RustPlusApi/Utils/ResponseHelper.cs`, replace both inline `new ErrorMessage { Message = message }` constructions with a shared factory, and add the parser:

```csharp
    /// <summary>Builds the <see cref="ErrorMessage"/> for a raw server identifier, or
    /// <see langword="null"/> when there is no error.</summary>
    /// <param name="message">The raw server error identifier, or <see langword="null"/>.</param>
    private static ErrorMessage? BuildError(string? message) =>
        message is null
            ? null
            : new ErrorMessage
            {
                Message = message,
                Code = ParseErrorCode(message)
            };

    /// <summary>Maps a raw Rust+ server error identifier to its <see cref="RustPlusErrorCode"/>;
    /// unrecognized identifiers map to <see cref="RustPlusErrorCode.Unknown"/>.</summary>
    /// <param name="message">The raw server error identifier.</param>
    private static RustPlusErrorCode ParseErrorCode(string message) => message switch
    {
        "server_error" => RustPlusErrorCode.ServerError,
        "banned" => RustPlusErrorCode.Banned,
        "rate_limit" => RustPlusErrorCode.RateLimit,
        "not_found" => RustPlusErrorCode.NotFound,
        "wrong_type" => RustPlusErrorCode.WrongType,
        "no_team" => RustPlusErrorCode.NoTeam,
        "no_clan" => RustPlusErrorCode.NoClan,
        "no_map" => RustPlusErrorCode.NoMap,
        "access_denied" => RustPlusErrorCode.AccessDenied,
        "message_not_sent" => RustPlusErrorCode.MessageNotSent,
        "too_many_subscribers" => RustPlusErrorCode.TooManySubscribers,
        "not_enabled" => RustPlusErrorCode.NotEnabled,
        _ => RustPlusErrorCode.Unknown,
    };
```

so the two builders become:

```csharp
        return new Response<T?>
        {
            IsSuccess = isSuccess,
            Error = BuildError(message),
            Data = data
        };
```

```csharp
        return new Response
        {
            IsSuccess = isSuccess,
            Error = BuildError(message)
        };
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/RustPlusApi.UnitTests --filter "FullyQualifiedName~ErrorCodeTests"`
Expected: PASS

- [ ] **Step 7: Run unit + integration suites**

Run: `dotnet test tests/RustPlusApi.UnitTests && dotnet test tests/RustPlusApi.IntegrationTests`
Expected: all PASS

- [ ] **Step 8: Commit**

```bash
git add src/RustPlusApi/Data/RustPlusErrorCode.cs src/RustPlusApi/Data/Response.cs src/RustPlusApi/Utils/ResponseHelper.cs tests/RustPlusApi.UnitTests/ErrorCodeTests.cs
git commit -m "feat: add machine-readable RustPlusErrorCode to error responses (M6)"
```

---

### Task 5: M5 — Robust FCM register-token parsing

`responseText.Split('=')[1]` truncates tokens containing `=`; `Contains("Error")` rejects any token containing that substring.

**Files:**

- Modify: `src/RustPlusApi.Fcm.Registration/Steps/AndroidFcmRegister.cs:173-176`
- Test: `tests/RustPlusApi.Fcm.Registration.UnitTests/AndroidFcmRegisterTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `AndroidFcmRegisterTests.cs`, mirroring the existing `RegisterFcmAsync_Success_ReturnsTokenAfterEquals` setup (same `Gcm` construction and `RegisterFcmAsync` arguments as the test at ~line 152):

```csharp
    [Fact]
    public async Task RegisterFcmAsync_TokenContainingEquals_ReturnsFullToken()
    {
        // The token is everything after the FIRST '=': a base64-ish token with '=' padding
        // must not be truncated at its own '='.
        var handler = StubHttpMessageHandler.Always(HttpStatusCode.OK, "token=abc=def==");
        var register = new AndroidFcmRegister(handler.CreateClient());

        var token = await register.RegisterFcmAsync(new RustPlusApi.Fcm.Data.Gcm
        {
            AndroidId = 1, SecurityToken = 2
        }, "fis-token");

        Assert.Equal("abc=def==", token);
    }

    [Fact]
    public async Task RegisterFcmAsync_TokenContainingErrorSubstring_Succeeds()
    {
        // Only the "Error=" key marks a failure; a token that merely contains the substring
        // "Error" is a valid success response and must not trigger the retry loop.
        var handler = StubHttpMessageHandler.Always(HttpStatusCode.OK, "token=AErrorB");
        var register = new AndroidFcmRegister(handler.CreateClient());

        var token = await register.RegisterFcmAsync(new RustPlusApi.Fcm.Data.Gcm
        {
            AndroidId = 1, SecurityToken = 2
        }, "fis-token");

        Assert.Equal("AErrorB", token);
        Assert.Single(handler.Requests); // no spurious retry
    }

    [Fact]
    public async Task RegisterFcmAsync_ResponseWithoutEquals_RetriesThenThrows()
    {
        // A malformed response (no key=value shape) used to crash on Split('=')[1];
        // now it is treated like an error response: retried, then surfaced cleanly.
        var handler = StubHttpMessageHandler.Always(HttpStatusCode.OK, "garbage");
        var register = new AndroidFcmRegister(handler.CreateClient());

        await Assert.ThrowsAsync<InvalidOperationException>(() => register.RegisterFcmAsync(
            new RustPlusApi.Fcm.Data.Gcm
            {
                AndroidId = 1, SecurityToken = 2
            }, "fis-token"));
    }
```

(If the existing tests pass `RegisterFcmAsync` different arguments — e.g. a named FIS token parameter — copy their exact call shape.)

- [ ] **Step 2: Run to verify the new tests fail**

Run: `dotnet test tests/RustPlusApi.Fcm.Registration.UnitTests --filter "FullyQualifiedName~RegisterFcmAsync_Token"`
Expected: `TokenContainingEquals` FAILS (gets `"abc"`), `TokenContainingErrorSubstring` FAILS (retries then throws), `ResponseWithoutEquals` FAILS (throws `IndexOutOfRangeException` instead of `InvalidOperationException`).

- [ ] **Step 3: Fix the parsing**

In `AndroidFcmRegister.cs`, replace lines 173–176:

```csharp
            if (!responseText.Contains("Error", StringComparison.Ordinal))
            {
                return responseText.Split('=')[1];
            }
```

with:

```csharp
            // Success is "token=<value>", failure is "Error=<reason>". Prefix-match the error key
            // and cut at the FIRST '=' so a token that itself contains '=' or the substring
            // "Error" is never mangled. A response without '=' is treated as a failure to retry.
            var separatorIndex = responseText.IndexOf('=');
            if (!responseText.StartsWith("Error=", StringComparison.Ordinal) && separatorIndex >= 0)
            {
                return responseText.Substring(separatorIndex + 1);
            }
```

(`Substring`, not a range expression — the file compiles for `netstandard2.0`.)

- [ ] **Step 4: Run the registration test project**

Run: `dotnet test tests/RustPlusApi.Fcm.Registration.UnitTests`
Expected: all PASS, including the pre-existing `RegisterFcmAsync_*` tests.

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusApi.Fcm.Registration/Steps/AndroidFcmRegister.cs tests/RustPlusApi.Fcm.Registration.UnitTests/AndroidFcmRegisterTests.cs
git commit -m "fix: parse FCM register token at first '=' and match Error= prefix (M5)"
```

---

### Task 6: L4 — Tear-free `_lastTraffic`

`DateTime` reads/writes across threads can tear on 32-bit `netstandard2.0` hosts. Store ticks in a `long` accessed via `Volatile`.

**Files:**

- Modify: `src/RustPlusApi.Fcm/RustPlusFcmSocket.cs` (field ~line 65; usages at ~209, ~383, ~403, ~516)

This is a memory-model correctness refactor with no observable behavior change on 64-bit test hosts — there is no meaningful new test. The existing heartbeat/inactivity-watchdog tests (`FcmSocketLifecycleTests`) exercise both the property reads and writes, keeping the coverage gate satisfied.

- [ ] **Step 1: Replace the field**

Replace:

```csharp
    /// <summary>UTC timestamp of the last received frame, observed by the inactivity watchdog.</summary>
    private DateTime _lastTraffic = DateTime.UtcNow;
```

with:

```csharp
    /// <summary>UTC tick count of the last received frame, observed by the inactivity watchdog.
    /// Stored as ticks behind <see cref="Volatile"/> because the watchdog reads it from another
    /// thread, and a non-volatile 64-bit read can tear on 32-bit netstandard2.0 hosts.</summary>
    private long _lastTrafficTicks = DateTime.UtcNow.Ticks;

    /// <summary>Tear-free accessor over <see cref="_lastTrafficTicks"/>.</summary>
    private DateTime LastTrafficUtc
    {
        get => new(Volatile.Read(ref _lastTrafficTicks), DateTimeKind.Utc);
        set => Volatile.Write(ref _lastTrafficTicks, value.Ticks);
    }
```

- [ ] **Step 2: Update the four usages**

- ~line 209 and ~383 and ~516: `_lastTraffic = DateTime.UtcNow;` → `LastTrafficUtc = DateTime.UtcNow;`
- ~line 403 (watchdog): `if (DateTime.UtcNow - _lastTraffic > _options.InactivityTimeout)` → `if (DateTime.UtcNow - LastTrafficUtc > _options.InactivityTimeout)`

Verify no other references remain: `grep -n "_lastTraffic\b" src/RustPlusApi.Fcm/RustPlusFcmSocket.cs` should only show `_lastTrafficTicks` lines.

- [ ] **Step 3: Run the FCM test project**

Run: `dotnet test tests/RustPlusApi.Fcm.UnitTests`
Expected: all PASS (lifecycle/watchdog suites cover both accessors)

- [ ] **Step 4: Commit**

```bash
git add src/RustPlusApi.Fcm/RustPlusFcmSocket.cs
git commit -m "fix: make last-traffic timestamp tear-free via Volatile over ticks (L4)"
```

---

### Task 7: L6 — Explicit null check for the deserialized notification body

`JsonSerializer.Deserialize<Body>("null")` returns `null`; the `bodyData!` forgiveness defers the failure to an NRE inside downstream handlers.

**Files:**

- Modify: `src/RustPlusApi.Fcm/RustPlusFcmSocket.cs:778-789` (`OnDataMessage`)
- Modify: `src/RustPlusApi.Fcm/RustPlusFcmSocketLog.cs` (new log message)
- Test: `tests/RustPlusApi.Fcm.UnitTests/FcmSocketFramingTests.cs`

- [ ] **Step 1: Write the failing test**

In `FcmSocketFramingTests.cs`, mirror the existing `LoginResponseThenDataMessage_RaisesNotificationReceived` test (~line 266) — same `NewSocket`/`Build`/`FirstFrame`/`NextFrame`/`RustNotification` helpers — but feed a JSON-`null` body:

```csharp
    [Fact]
    public async Task DataMessage_BodyDeserializesToNull_IsSkippedWithoutFaulting()
    {
        // JsonSerializer.Deserialize<Body>("null") returns null; the message must be skipped
        // with a log instead of deferring an NRE into downstream event handlers.
        using var socket = NewSocket();
        string? notification = null;
        Exception? error = null;
        socket.NotificationReceived += (_, n) => notification = n;
        socket.ErrorOccurred += (_, ex) => error = ex;

        var script = Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            NextFrame(McsProtoTag.KDataMessageStanzaTag, RustNotification(body: "null")));

        await socket.RunReceiveLoopOverStreamAsync(new ScriptedStream(script));

        Assert.Null(notification); // skipped, not dispatched with a null Body
        Assert.Null(error);        // and skipped cleanly, not via the catch-all
    }
```

(Copy the exact `FirstFrame(McsProtoTag.KLoginResponseTag, ...)` shape from the neighbouring test — if it builds the login frame differently, match it.)

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/RustPlusApi.Fcm.UnitTests --filter "FullyQualifiedName~BodyDeserializesToNull"`
Expected: FAIL — currently the message is dispatched with a null `Body` (notification non-null) or faults downstream (error non-null), depending on the handler path.

- [ ] **Step 3: Add the log message**

In `RustPlusFcmSocketLog.cs`, add alongside `LogNotRustNotification`:

```csharp
    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification body deserialized to null; message skipped.")]
    public static partial void LogNullNotificationBody(this ILogger logger);
```

- [ ] **Step 4: Add the null check**

In `OnDataMessage` (`RustPlusFcmSocket.cs` ~line 778), replace:

```csharp
        var bodyData = JsonSerializer.Deserialize<Body>(body, _parsingOptions);
```

with:

```csharp
        var bodyData = JsonSerializer.Deserialize<Body>(body, _parsingOptions);
        if (bodyData is null)
        {
            // A JSON-literal "null" body deserializes to null without throwing; skip it here
            // instead of deferring a NullReferenceException into downstream event handlers.
            Logger.LogNullNotificationBody();
            return;
        }
```

and change `Body = bodyData!,` to `Body = bodyData,` (the forgiveness operator is no longer needed).

- [ ] **Step 5: Run the FCM test project**

Run: `dotnet test tests/RustPlusApi.Fcm.UnitTests`
Expected: all PASS

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusApi.Fcm/RustPlusFcmSocket.cs src/RustPlusApi.Fcm/RustPlusFcmSocketLog.cs tests/RustPlusApi.Fcm.UnitTests/FcmSocketFramingTests.cs
git commit -m "fix: skip FCM messages whose body deserializes to null (L6)"
```

---

### Task 8: L1 — Single-source options snapshot via `Clone()`

The member-by-member snapshot in each socket constructor silently drops any newly added option. Move the copy knowledge onto the options classes.

**Files:**

- Modify: `src/RustPlusApi/RustPlusSocketOptions.cs`, `src/RustPlusApi/RustPlusSocket.cs:31-39`
- Modify: `src/RustPlusApi.Fcm/RustPlusFcmSocketOptions.cs`, `src/RustPlusApi.Fcm/RustPlusFcmSocket.cs:46-51`
- Test: `tests/RustPlusApi.UnitTests/RustPlusSocketOptionsTests.cs` (new), `tests/RustPlusApi.Fcm.UnitTests/RustPlusFcmSocketOptionsTests.cs` (new)

- [ ] **Step 1: Write the failing tests**

Create `tests/RustPlusApi.UnitTests/RustPlusSocketOptionsTests.cs`:

```csharp
using Xunit;

namespace RustPlusApi.UnitTests;

/// <summary>Clone is the single place that knows every option; this guards it copying all of them.</summary>
public class RustPlusSocketOptionsTests
{
    [Fact]
    public void Clone_CopiesEveryPublicProperty()
    {
        var options = new RustPlusSocketOptions
        {
            RequestTimeout = TimeSpan.FromSeconds(1),
            KeepAliveInterval = TimeSpan.FromSeconds(2),
            TeardownTimeout = TimeSpan.FromSeconds(3),
            ReceiveBufferSize = 1234,
        };

        var clone = options.Clone();

        Assert.NotSame(options, clone);
        // Reflection sweep: a property added later but forgotten in Clone() fails here as long as
        // it is also initialised above to a non-default value — extend both together.
        foreach (var property in typeof(RustPlusSocketOptions).GetProperties())
        {
            Assert.Equal(property.GetValue(options), property.GetValue(clone));
        }
    }
}
```

Create `tests/RustPlusApi.Fcm.UnitTests/RustPlusFcmSocketOptionsTests.cs`:

```csharp
using Xunit;

namespace RustPlusApi.Fcm.UnitTests;

/// <summary>Clone is the single place that knows every option; this guards it copying all of them.</summary>
public class RustPlusFcmSocketOptionsTests
{
    [Fact]
    public void Clone_CopiesEveryPublicProperty()
    {
        var options = new RustPlusFcmSocketOptions
        {
            HeartbeatInterval = TimeSpan.FromMinutes(1), InactivityTimeout = TimeSpan.FromMinutes(2),
        };

        var clone = options.Clone();

        Assert.NotSame(options, clone);
        foreach (var property in typeof(RustPlusFcmSocketOptions).GetProperties())
        {
            Assert.Equal(property.GetValue(options), property.GetValue(clone));
        }
    }
}
```

- [ ] **Step 2: Run to verify they fail to compile**

Run: `dotnet test tests/RustPlusApi.UnitTests --filter "FullyQualifiedName~OptionsTests"`
Expected: BUILD FAILURE — `Clone` does not exist. (Both unit test projects have `InternalsVisibleTo`.)

- [ ] **Step 3: Add `Clone()` to both options classes**

`RustPlusSocketOptions.cs`:

```csharp
    /// <summary>Creates a copy of this instance — the single place that knows every option, so the
    /// per-socket snapshot cannot silently miss a newly added property.</summary>
    internal RustPlusSocketOptions Clone() => new()
    {
        RequestTimeout = RequestTimeout,
        KeepAliveInterval = KeepAliveInterval,
        TeardownTimeout = TeardownTimeout,
        ReceiveBufferSize = ReceiveBufferSize,
    };
```

`RustPlusFcmSocketOptions.cs`:

```csharp
    /// <summary>Creates a copy of this instance — the single place that knows every option, so the
    /// per-socket snapshot cannot silently miss a newly added property.</summary>
    internal RustPlusFcmSocketOptions Clone() => new()
    {
        HeartbeatInterval = HeartbeatInterval, InactivityTimeout = InactivityTimeout,
    };
```

- [ ] **Step 4: Use it in both socket constructors**

`RustPlusSocket.cs` lines 31–39 become:

```csharp
    /// <summary>Tuning values for this instance — a private snapshot taken at construction, so later
    /// mutation of the caller's (possibly shared) options object cannot affect a live socket.</summary>
    private readonly RustPlusSocketOptions _options = options?.Clone() ?? new RustPlusSocketOptions();
```

`RustPlusFcmSocket.cs` lines 46–51 become:

```csharp
    /// <summary>Tuning values for this instance — a private snapshot taken at construction, so later
    /// mutation of the caller's (possibly shared) options object cannot affect a live socket.</summary>
    private readonly RustPlusFcmSocketOptions _options = options?.Clone() ?? new RustPlusFcmSocketOptions();
```

- [ ] **Step 5: Run both unit test projects**

Run: `dotnet test tests/RustPlusApi.UnitTests && dotnet test tests/RustPlusApi.Fcm.UnitTests`
Expected: all PASS (including any pre-existing options-snapshot tests)

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusApi/RustPlusSocketOptions.cs src/RustPlusApi/RustPlusSocket.cs src/RustPlusApi.Fcm/RustPlusFcmSocketOptions.cs src/RustPlusApi.Fcm/RustPlusFcmSocket.cs tests/RustPlusApi.UnitTests/RustPlusSocketOptionsTests.cs tests/RustPlusApi.Fcm.UnitTests/RustPlusFcmSocketOptionsTests.cs
git commit -m "refactor: single-source options snapshots via Clone() (L1)"
```

---

### Task 9: L7 — Prompt cancellation for the Steam login HTTP listener

`HttpListener.GetContextAsync()` takes no token; cancelling `LoginAsync` only takes effect after the next request arrives (a hung console app on Ctrl-C).

**Files:**

- Modify: `src/RustPlusApi.Fcm.Registration/Steps/SteamLoginService.cs:42-81` (`LoginAsync`)

No test: this class drives a live Chrome process and is `[ExcludeFromCodeCoverage]` by design (verify the attribute/justification is present on the class; the live flow is validated by running the `RustPlus.Register.ConsoleApp` sample).

- [ ] **Step 1: Register the cancellation callback and contain the resulting exceptions**

In `LoginAsync`, right after `listener.Start();` (~line 46), add:

```csharp
        // GetContextAsync takes no cancellation token: stopping the listener is the only way to
        // unblock the wait promptly, so cancellation must not have to wait for the next request.
        using var cancellationRegistration = cancellationToken.Register(listener.Stop);
```

Then change the body of the `while` loop (~line 63) so the stopped-listener exceptions surface as cancellation:

```csharp
            while (!cancellationToken.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
                {
                    // The listener was stopped under the wait — by the cancellation registration
                    // above (expected; rethrow as cancellation) or by an unrelated teardown.
                    cancellationToken.ThrowIfCancellationRequested();
                    throw;
                }

                var token = await ReadTokenAsync(context.Request).ConfigureAwait(false);
                await RespondAsync(context, "<h1>Done. You can close this window.</h1>").ConfigureAwait(false);
                if (!string.IsNullOrEmpty(token))
                {
                    return token!;
                }
            }
```

(The `finally` block's existing `listener.Stop()` stays — stopping twice is harmless.)

- [ ] **Step 2: Build the registration package (warnings-as-errors is the gate here)**

Run: `dotnet build src/RustPlusApi.Fcm.Registration`
Expected: build succeeds, 0 warnings

- [ ] **Step 3: Run the registration test project (regression only)**

Run: `dotnet test tests/RustPlusApi.Fcm.Registration.UnitTests`
Expected: all PASS

- [ ] **Step 4: Commit**

```bash
git add src/RustPlusApi.Fcm.Registration/Steps/SteamLoginService.cs
git commit -m "fix: make SteamLoginService cancellation unblock the HTTP listener promptly (L7)"
```

---

### Task 10: L8 — Restrict credentials file permissions and document sensitivity

`CredentialsStore.Save` writes GCM/FCM/Expo tokens world-readable on typical umasks, and the docs don't flag the file as sensitive.

**Files:**

- Modify: `src/RustPlusApi.Fcm.Registration/CredentialsStore.cs:32-33`
- Modify: `docs/articles/credentials.md`
- Test: `tests/RustPlusApi.Fcm.Registration.UnitTests/CredentialsStoreFileTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `CredentialsStoreFileTests.cs`:

```csharp
#if NET10_0_OR_GREATER
    [Fact]
    public void Save_OnUnix_RestrictsFileModeToOwner()
    {
        // The file holds long-lived push credentials; other local users must not be able to read it.
        if (OperatingSystem.IsWindows())
        {
            return; // Windows has no unix file modes; ACLs are out of scope.
        }

        var path = Path.Combine(Path.GetTempPath(), $"creds-{Guid.NewGuid():N}.json");
        try
        {
            CredentialsStore.Save(path, new Credentials
            {
                Gcm = new Gcm
                {
                    AndroidId = 1, SecurityToken = 2
                }
            });

            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
#endif
```

(The `#if` matters: the net8.0 test run resolves the `netstandard2.0` build of the library, which cannot set file modes; only the net10.0 run asserts this.)

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/RustPlusApi.Fcm.Registration.UnitTests --filter "FullyQualifiedName~RestrictsFileMode" -f net10.0`
Expected: FAIL — mode is the umask default (e.g. `644`), not `600`.

- [ ] **Step 3: Restrict the mode in `Save`**

Replace the expression-bodied `Save` in `CredentialsStore.cs`:

```csharp
    /// <summary>Serializes <paramref name="credentials"/> and writes the result to <paramref name="path"/>.
    /// The file contains long-lived push credentials (GCM security token, FCM/Expo tokens) in plain
    /// JSON — treat it like a password file. On .NET 10+ on Unix it is restricted to owner read/write.</summary>
    /// <param name="path">The file path to write to.</param>
    /// <param name="credentials">The credentials to persist.</param>
    public static void Save(string path, Credentials credentials)
    {
        File.WriteAllText(path, Serialize(credentials));
#if NET10_0_OR_GREATER
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
#endif
    }
```

- [ ] **Step 4: Document the sensitivity**

In `docs/articles/credentials.md`, in the "Loading credentials back" section (end of file), add after the code block:

```markdown
> [!WARNING]
> `rustplus.config.json` contains long-lived push credentials (the GCM security token and
> FCM/Expo tokens) in plain JSON — anyone who can read the file can receive your pairing
> notifications. Treat it like a password file: keep it out of version control and shared
> directories. On .NET 10+ on Linux/macOS, `CredentialsStore.Save` restricts it to owner
> read/write (`600`); on other targets, restrict permissions yourself.
```

- [ ] **Step 5: Run the registration test project on both TFMs**

Run: `dotnet test tests/RustPlusApi.Fcm.Registration.UnitTests`
Expected: all PASS on net8.0 and net10.0

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusApi.Fcm.Registration/CredentialsStore.cs docs/articles/credentials.md tests/RustPlusApi.Fcm.Registration.UnitTests/CredentialsStoreFileTests.cs
git commit -m "fix: restrict credentials file to owner read/write and document sensitivity (L8)"
```

---

### Task 11: L2 + L3 + L5 — Documentation-accuracy fixes

Three findings where the right fix is honest docs, not behavior changes: `RequestSent` fires on enqueue (changing when it fires would alter observable ordering for existing consumers — document reality instead); the switch-vs-alarm heuristic is explained only in a code comment; `persistentIds` growth characteristics are undocumented.

**Files:**

- Modify: `src/RustPlusApi/RustPlusSocket.cs:61-71` (event docs)
- Modify: `src/RustPlusApi/RustPlus.cs:32-41` (event docs)
- Modify: `src/RustPlusApi.Fcm/RustPlusFcmSocket.cs:25` and `src/RustPlusApi.Fcm/RustPlusFcm.cs` (param docs)
- Modify: `docs/articles/fcm-notifications.md`

- [ ] **Step 1: L2 — make the send-event docs match what they observe**

In `RustPlusSocket.cs`, replace the two event docs:

```csharp
    /// <summary>
    /// Occurs when a request is about to be queued for transmission.
    /// </summary>
    /// <seealso cref="SendRequestAsync"/>
    public event EventHandler? SendingRequest;

    /// <summary>
    /// Occurs when a request has been queued for transmission. The request is handed to the
    /// background send loop at this point; the actual WebSocket send happens asynchronously
    /// shortly after, so this event does not confirm the bytes left the machine.
    /// </summary>
    /// <seealso cref="SendRequestAsync"/>
    public event EventHandler<AppRequest>? RequestSent;
```

- [ ] **Step 2: L3 — document the switch-vs-alarm heuristic on the public events**

In `RustPlus.cs`, replace the two event docs:

```csharp
    /// <summary>
    /// Occurs when a <see cref="SmartSwitchEventArg"/> is triggered by a smart switch or a smart
    /// alarm. The Rust+ protocol does not distinguish the two: an <c>EntityChanged</c> broadcast
    /// whose payload carries no item capacity is routed here, so alarm state changes also surface
    /// through this event.
    /// </summary>
    public event EventHandler<SmartSwitchEventArg>? OnSmartSwitchTriggered;

    /// <summary>
    /// Occurs when a <see cref="StorageMonitorEventArg"/> is triggered by a storage monitor
    /// (an <c>EntityChanged</c> broadcast whose payload carries item capacity).
    /// </summary>
    public event EventHandler<StorageMonitorEventArg>? OnStorageMonitorTriggered;
```

- [ ] **Step 3: L5 — document the recommended `persistentIds` collection type**

In `RustPlusFcmSocket.cs` (line 25) — and the same `<param>` on `RustPlusFcm`'s constructor docs if it repeats it — replace:

```csharp
/// <param name="persistentIds">The collection of persistent IDs as <see cref="ICollection{T}"/> of <see cref="string"/>.</param>
```

with:

```csharp
/// <param name="persistentIds">Already-processed message IDs, used for de-duplication. Every data
/// message is checked against and appended to this collection, so for a long-lived listener prefer
/// a set-like implementation (e.g. <see cref="HashSet{T}"/>) — with a <see cref="List{T}"/> the
/// duplicate check is a linear scan that degrades as the collection grows unboundedly.</param>
```

In `docs/articles/fcm-notifications.md`, the reconnect-helper example (~line 91) declares `ICollection<string> persistentIds = new List<string>();` — change it to:

```csharp
ICollection<string> persistentIds = new HashSet<string>();
```

and add one sentence to the prose at ~line 29 (after "already-seen notification IDs to skip on"):

```markdown
Prefer a `HashSet<string>` — the collection is consulted on every message and grows for the
lifetime of the listener, so a `List<string>`'s linear scan degrades over long sessions.
```

- [ ] **Step 4: Build everything (XML docs are compiled, warnings-as-errors)**

Run: `dotnet build`
Expected: build succeeds, 0 warnings

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusApi/RustPlusSocket.cs src/RustPlusApi/RustPlus.cs src/RustPlusApi.Fcm/RustPlusFcmSocket.cs src/RustPlusApi.Fcm/RustPlusFcm.cs docs/articles/fcm-notifications.md
git commit -m "docs: honest event semantics, switch-vs-alarm heuristic, persistentIds guidance (L2, L3, L5)"
```

---

### Task 12: Full verification

- [ ] **Step 1: Full build + full test suite (both TFMs)**

Run: `dotnet build && dotnet test`
Expected: 0 warnings; all ~312+ tests PASS on net8.0 and net10.0.

- [ ] **Step 2: Coverage gate**

Run the repo's coverage command the same way CI does (check `.github/workflows/` for the exact invocation, typically `dotnet test` with the `coverlet.runsettings` per project) and confirm ≥95% line / ≥90% branch still holds. The new catch blocks (Task 1) and guards (Tasks 2, 7) all have dedicated tests, so the gate should hold; if a line is uncovered, add a test rather than an exclusion.

- [ ] **Step 3: Confirm with the user before any push/PR**

Per standing preference: nothing is pushed without explicit say-so. Offer a summary of the 11 commits and ask whether to push / open a PR against `develop`.
