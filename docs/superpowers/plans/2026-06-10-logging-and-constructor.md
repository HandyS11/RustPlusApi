# Library Logging & Constructor Clarity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `ILogger`-based consumer observability to the two socket libraries and replace `RustPlus`'s 6-parameter constructor with a `RustPlusConnection` record, as a clean (breaking) v2-style change.

**Architecture:** Logging uses `Microsoft.Extensions.Logging.Abstractions`; each socket resolves one `ILogger` from an optional `ILoggerFactory` carried on its existing options object, defaulting to `NullLogger`. All `Debug.WriteLine` calls become `[LoggerMessage]` source-generated, leveled, structured log methods. Connection identity (server/port/ids/proxy) is grouped into a `RustPlusConnection` record so the public constructor reads clearly.

**Tech Stack:** C# (multi-target `netstandard2.0` + `net10.0`), xUnit, protobuf-net, Microsoft.Extensions.Logging.Abstractions 10.0.8, Central Package Management, coverlet + ReportGenerator coverage gate, Stryker mutation testing, DocFX.

**Spec:** `docs/superpowers/specs/2026-06-10-logging-and-constructor-design.md`

**Conventions in this repo:**

- Commits are **not** auto-made by tooling; this plan includes explicit commit steps. (Repo preference: commit only when steps say so.)
- `dotnet build` / `dotnet test` from the repo root use the solution `RustPlusApi.sln`.
- Both libraries multi-target; always build/test both TFMs (the default `dotnet test` runs the matrix).

---

## File Structure

**Created:**

- `src/RustPlusApi/RustPlusConnection.cs` — the connection-identity record.
- `src/RustPlusApi/RustPlusSocketLog.cs` — `[LoggerMessage]` log methods for the core socket.
- `src/RustPlusApi.Fcm/RustPlusFcmSocketLog.cs` — `[LoggerMessage]` log methods for the FCM socket.
- `tests/RustPlusApi.UnitTests/SpyLogger.cs` — in-memory test logger + factory (core).
- `tests/RustPlusApi.UnitTests/RustPlusLoggingTests.cs` — logger-resolution + emission tests (core).
- `tests/RustPlusApi.Fcm.UnitTests/SpyLogger.cs` — in-memory test logger + factory (FCM).
- `tests/RustPlusApi.Fcm.UnitTests/FcmLoggingTests.cs` — logger-resolution + emission tests (FCM).

**Modified:**

- `Directory.Packages.props` — add the logging package version.
- `src/RustPlusApi/RustPlusApi.csproj`, `src/RustPlusApi.Fcm/RustPlusApi.Fcm.csproj` — reference the package.
- `src/RustPlusApi/RustPlusSocketOptions.cs`, `src/RustPlusApi.Fcm/RustPlusFcmSocketOptions.cs` — add `LoggerFactory`.
- `src/RustPlusApi/RustPlusSocket.cs`, `src/RustPlusApi/RustPlus.cs` — new constructor, logger field, replace `Debug.WriteLine`.
- `src/RustPlusApi.Fcm/RustPlusFcmSocket.cs`, `src/RustPlusApi.Fcm/RustPlusFcm.cs` — logger field, replace `Debug.WriteLine`.
- All `RustPlusApi.IntegrationTests` files + `tests/RustPlusApi.NetFrameworkSmoke/Program.cs` — migrate `new RustPlus(...)` call sites.
- `tests/RustPlusApi.Fcm.UnitTests/stryker-config.json` (+ Camera/Registration configs for alignment) — `ignore-methods`.
- `docs/development/testing.md`, both `README.md`s, DocFX articles + `docs/toc.yml`.

---

## Task 1: Add the logging dependency

**Files:**

- Modify: `Directory.Packages.props`
- Modify: `src/RustPlusApi/RustPlusApi.csproj`
- Modify: `src/RustPlusApi.Fcm/RustPlusApi.Fcm.csproj`

- [ ] **Step 1: Add the package version (CPM)**

In `Directory.Packages.props`, add this line inside the first `<ItemGroup>` (next to the other 10.0.x entries):

```xml
<PackageVersion Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.8" />
```

- [ ] **Step 2: Reference it from the core library**

In `src/RustPlusApi/RustPlusApi.csproj`, add to the unconditional `<ItemGroup>` that already holds `protobuf-net`:

```xml
<PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
```

- [ ] **Step 3: Reference it from the FCM library**

In `src/RustPlusApi.Fcm/RustPlusApi.Fcm.csproj`, add to the unconditional `<ItemGroup>` that already holds `protobuf-net`:

```xml
<PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
```

- [ ] **Step 4: Restore and build to verify the dependency resolves on both TFMs**

Run: `dotnet build src/RustPlusApi/RustPlusApi.csproj src/RustPlusApi.Fcm/RustPlusApi.Fcm.csproj`
Expected: Build succeeded, 0 errors (both `netstandard2.0` and `net10.0`).

- [ ] **Step 5: Commit**

```bash
git add Directory.Packages.props src/RustPlusApi/RustPlusApi.csproj src/RustPlusApi.Fcm/RustPlusApi.Fcm.csproj
git commit -m "build: add Microsoft.Extensions.Logging.Abstractions to socket libraries"
```

---

## Task 2: Add the `RustPlusConnection` record

**Files:**

- Create: `src/RustPlusApi/RustPlusConnection.cs`

- [ ] **Step 1: Create the record with migrated XML docs**

Create `src/RustPlusApi/RustPlusConnection.cs`. The per-parameter wording is copied verbatim from the old `RustPlus`/`RustPlusSocket` constructor docs so no documentation is lost:

```csharp
namespace RustPlusApi;

/// <summary>
/// Connection identity for a <see cref="RustPlus"/> client: the server endpoint and the player
/// credentials a request is issued as. Grouping these into one value keeps the
/// <see cref="RustPlus"/> constructor readable at the call site.
/// </summary>
/// <param name="Server">The IP address of the Rust+ server.</param>
/// <param name="Port">The port dedicated for the Rust+ companion app (not the one used to connect in-game).</param>
/// <param name="PlayerId">Your Steam ID.</param>
/// <param name="PlayerToken">Your player token acquired with FCM.</param>
/// <param name="UseFacepunchProxy">Specifies whether to use the Facepunch proxy.</param>
public sealed record RustPlusConnection(
    string Server,
    int Port,
    ulong PlayerId,
    int PlayerToken,
    bool UseFacepunchProxy = false);
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/RustPlusApi/RustPlusApi.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/RustPlusApi/RustPlusConnection.cs
git commit -m "feat: add RustPlusConnection record for client connection identity"
```

---

## Task 3: Migrate the `RustPlus`/`RustPlusSocket` constructor (no logging yet)

This is a clean break: the old 6-parameter constructor is removed. The full test suite is the safety net — it must compile and stay green after every call site is migrated.

**Files:**

- Modify: `src/RustPlusApi/RustPlusSocket.cs:23-29` (primary constructor), `:130` (`_playerToken`), `:133` (`PlayerId`), `:176-178` (URI build)
- Modify: `src/RustPlusApi/RustPlus.cs:27-28` (primary constructor + base call)
- Modify: all `tests/RustPlusApi.IntegrationTests/*.cs` call sites, `tests/RustPlusApi.NetFrameworkSmoke/Program.cs`

- [ ] **Step 1: Change the `RustPlusSocket` primary constructor**

In `src/RustPlusApi/RustPlusSocket.cs`, replace the class header (currently lines 23-30):

```csharp
public abstract class RustPlusSocket(
    string server,
    int port,
    ulong playerId,
    int playerToken,
    bool useFacepunchProxy = false,
    RustPlusSocketOptions? options = null)
    : IRustPlusSocket, IDisposable, IAsyncDisposable
```

with:

```csharp
public abstract class RustPlusSocket(
    RustPlusConnection connection,
    RustPlusSocketOptions? options = null)
    : IRustPlusSocket, IDisposable, IAsyncDisposable
```

Also replace the class-level XML doc `<param>` block above it (lines 17-22) so it documents the new parameters:

```csharp
/// <summary>
/// A Rust+ API client made in C#.
/// </summary>
/// <param name="connection">The server endpoint and player credentials to connect as.</param>
/// <param name="options">Tuning options (timeouts, keep-alive, buffer size); defaults are used when <see langword="null"/>.</param>
```

- [ ] **Step 2: Update the field initializers and URI build to read from `connection`**

In `src/RustPlusApi/RustPlusSocket.cs`:

- Line ~130: `private int _playerToken = playerToken;` → `private int _playerToken = connection.PlayerToken;`
- Line ~133: `protected ulong PlayerId { get; private set; } = playerId;` → `protected ulong PlayerId { get; private set; } = connection.PlayerId;`
- Lines ~176-178, the URI build, replace:

```csharp
        var uri = useFacepunchProxy
            ? new Uri($"wss://companion-rust.facepunch.com/game/{server}/{port}")
            : new Uri($"ws://{server}:{port}");
```

with:

```csharp
        var uri = connection.UseFacepunchProxy
            ? new Uri($"wss://companion-rust.facepunch.com/game/{connection.Server}/{connection.Port}")
            : new Uri($"ws://{connection.Server}:{connection.Port}");
```

- [ ] **Step 3: Change the `RustPlus` constructor + base call**

In `src/RustPlusApi/RustPlus.cs`, replace the class header (lines 27-28):

```csharp
public class RustPlus(string server, int port, ulong playerId, int playerToken, bool useFacepunchProxy = false, RustPlusSocketOptions? options = null)
    : RustPlusSocket(server, port, playerId, playerToken, useFacepunchProxy, options), IRustPlus
```

with:

```csharp
public class RustPlus(RustPlusConnection connection, RustPlusSocketOptions? options = null)
    : RustPlusSocket(connection, options), IRustPlus
```

Replace the class-level XML doc `<param>` block above it (lines 16-26) so it documents the new parameters and keeps the `<seealso>`:

```csharp
/// <summary>
/// Initializes a new instance of the <see cref="RustPlus"/> class,
/// connecting to a Rust+ server using the specified parameters.
/// </summary>
/// <param name="connection">The server endpoint and player credentials to connect as.</param>
/// <param name="options">Tuning options (timeouts, keep-alive, buffer size); defaults are used when <see langword="null"/>.</param>
/// <seealso cref="RustPlusSocket"/>
```

- [ ] **Step 4: Build the production libraries to confirm they compile**

Run: `dotnet build src/RustPlusApi/RustPlusApi.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Migrate the uniform integration-test call sites**

The dominant call form takes four args. Run these exact-string replacements across the integration tests (safe because the strings are unambiguous):

```bash
cd tests/RustPlusApi.IntegrationTests
grep -rl 'new RustPlus(' . | while read -r f; do
  sed -i \
    -e 's/new RustPlus(MockRustPlusServer\.Host, server\.Port, PlayerId, PlayerToken)/new RustPlus(new RustPlusConnection(MockRustPlusServer.Host, server.Port, PlayerId, PlayerToken))/g' \
    -e 's/new RustPlus(MockRustPlusServer\.Host, 1, PlayerId, PlayerToken)/new RustPlus(new RustPlusConnection(MockRustPlusServer.Host, 1, PlayerId, PlayerToken))/g' \
    -e 's/new RustPlus(MockRustPlusServer\.Host, server\.Port, 1, 1)/new RustPlus(new RustPlusConnection(MockRustPlusServer.Host, server.Port, 1, 1))/g' \
    "$f"
done
cd ../..
```

- [ ] **Step 6: Migrate the non-uniform call sites by hand**

These carry an `options` argument, custom literals, or `useFacepunchProxy` and must keep `options` as the **second** argument to `RustPlus` (outside the record):

In `tests/RustPlusApi.IntegrationTests/SocketErrorTests.cs`:

- `new RustPlus(MockRustPlusServer.Host, server.Port, 1, 1, options: options)` → `new RustPlus(new RustPlusConnection(MockRustPlusServer.Host, server.Port, 1, 1), options)`
- `new RustPlus("127.0.0.1", 1, 1, 1)` → `new RustPlus(new RustPlusConnection("127.0.0.1", 1, 1, 1))`
- `new RustPlus("example.invalid", 28083, 1, 1, useFacepunchProxy: true)` → `new RustPlus(new RustPlusConnection("example.invalid", 28083, 1, 1, useFacepunchProxy: true))`

In `tests/RustPlusApi.NetFrameworkSmoke/Program.cs`:

- `new RustPlus("127.0.0.1", 28083, 76561198000000000UL, 123456789)` → `new RustPlus(new RustPlusConnection("127.0.0.1", 28083, 76561198000000000UL, 123456789))`

- [ ] **Step 7: Confirm no old-form call sites remain**

Run: `grep -rn 'new RustPlus(' tests/ | grep -v 'new RustPlusConnection' | grep -v 'new RustPlusFcm'`
Expected: no output (every `RustPlus` client construction now wraps a `RustPlusConnection`).

- [ ] **Step 8: Build the whole solution**

Run: `dotnet build`
Expected: Build succeeded, 0 errors. (If any call site was missed, the compiler error names the file and line — fix it the same way.)

- [ ] **Step 9: Run the full test suite**

Run: `dotnet test`
Expected: All tests pass. Behaviour is unchanged; only the constructor grouping changed.

- [ ] **Step 10: Commit**

```bash
git add src/RustPlusApi/RustPlusSocket.cs src/RustPlusApi/RustPlus.cs tests/
git commit -m "refactor!: group RustPlus connection params into RustPlusConnection record"
```

---

## Task 4: Core logging — options, logger field, log methods, replace Debug.WriteLine

**Files:**

- Modify: `src/RustPlusApi/RustPlusSocketOptions.cs`
- Create: `src/RustPlusApi/RustPlusSocketLog.cs`
- Modify: `src/RustPlusApi/RustPlusSocket.cs` (add logger field; replace `Debug.WriteLine`)
- Modify: `src/RustPlusApi/RustPlus.cs` (replace `Debug.WriteLine`)
- Create: `tests/RustPlusApi.UnitTests/SpyLogger.cs`
- Create: `tests/RustPlusApi.UnitTests/RustPlusLoggingTests.cs`

- [ ] **Step 1: Write the failing logger-resolution + emission tests**

Create `tests/RustPlusApi.UnitTests/SpyLogger.cs`:

```csharp
using Microsoft.Extensions.Logging;

namespace RustPlusApi.UnitTests;

/// <summary>In-memory logger capturing entries so tests can assert level + message.</summary>
public sealed class SpyLogger : ILogger
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
        => Entries.Add((logLevel, formatter(state, exception)));

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}

/// <summary>Factory that hands out a single shared <see cref="SpyLogger"/> for assertions.</summary>
public sealed class SpyLoggerFactory : ILoggerFactory
{
    public SpyLogger Logger { get; } = new();
    public ILogger CreateLogger(string categoryName) => Logger;
    public void AddProvider(ILoggerProvider provider) { }
    public void Dispose() { }
}
```

Create `tests/RustPlusApi.UnitTests/RustPlusLoggingTests.cs`:

```csharp
using Microsoft.Extensions.Logging;
using RustPlusApi.Data;

namespace RustPlusApi.UnitTests;

public class RustPlusLoggingTests
{
    private static RustPlusConnection AnyConnection() => new("127.0.0.1", 1, 1UL, 1);

    [Fact]
    public void Constructor_WithNoOptions_DoesNotThrow()
    {
        using var client = new RustPlus(AnyConnection());
        Assert.False(client.IsConnected);
    }

    [Fact]
    public void Constructor_WithOptionsButNoFactory_DoesNotThrow()
    {
        using var client = new RustPlus(AnyConnection(), new RustPlusSocketOptions());
        Assert.False(client.IsConnected);
    }

    [Fact]
    public void Constructor_WithLoggerFactory_DoesNotThrow()
    {
        var factory = new SpyLoggerFactory();
        using var client = new RustPlus(AnyConnection(), new RustPlusSocketOptions { LoggerFactory = factory });
        Assert.False(client.IsConnected);
    }

    [Fact]
    public void UnknownBroadcast_LogsWarning()
    {
        var factory = new SpyLoggerFactory();
        using var client = new TestableRustPlus(new RustPlusSocketOptions { LoggerFactory = factory });

        client.InvokeParseNotification(new AppBroadcast());

        Assert.Contains(factory.Logger.Entries, e => e.Level == LogLevel.Warning);
    }

    /// <summary>Exposes the protected ParseNotification so the unknown-broadcast path can be driven.</summary>
    private sealed class TestableRustPlus(RustPlusSocketOptions options)
        : RustPlus(new RustPlusConnection("127.0.0.1", 1, 1UL, 1), options)
    {
        public void InvokeParseNotification(AppBroadcast broadcast) => ParseNotification(broadcast);
    }
}
```

Note: `ParseNotification` is `protected` on `RustPlus`; the nested `TestableRustPlus` re-exposes it. An empty `AppBroadcast` (no `EntityChanged`/`TeamMessage`/… set) falls through to the unknown-broadcast log path.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/RustPlusApi.UnitTests/RustPlusApi.UnitTests.csproj --filter RustPlusLoggingTests`
Expected: FAIL — `RustPlusSocketOptions` has no `LoggerFactory` property (compile error).

- [ ] **Step 3: Add `LoggerFactory` to `RustPlusSocketOptions`**

In `src/RustPlusApi/RustPlusSocketOptions.cs`, add `using Microsoft.Extensions.Logging;` at the top, and this property inside the class:

```csharp
    /// <summary>Factory used to create the client's logger. When <see langword="null"/>, logging is
    /// disabled (a no-op <c>NullLogger</c> is used). Supply one to route diagnostics into your
    /// logging stack.</summary>
    public ILoggerFactory? LoggerFactory { get; init; }
```

- [ ] **Step 4: Add the core log methods**

Create `src/RustPlusApi/RustPlusSocketLog.cs`. Method names are `Log`-prefixed so the Stryker `Log*` ignore wildcard (Task 6) covers them, and they are extension methods on `ILogger` for clean call sites:

```csharp
using Microsoft.Extensions.Logging;
using RustPlusContracts;

namespace RustPlusApi;

/// <summary>Source-generated, structured log messages for <see cref="RustPlusSocket"/> and
/// <see cref="RustPlus"/>. Generated bodies carry <c>[GeneratedCode]</c> and are excluded from the
/// coverage gate automatically.</summary>
internal static partial class RustPlusSocketLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Receiving data from the Rust+ server.")]
    public static partial void LogReceivingData(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Waiting for data.")]
    public static partial void LogWaitingForData(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Receive loop exited.")]
    public static partial void LogReceiveLoopExited(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Received message: {Message}")]
    public static partial void LogReceivedMessage(this ILogger logger, AppMessage message);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Received notification: {Message}")]
    public static partial void LogReceivedNotification(this ILogger logger, AppMessage message);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Received response: {Message}")]
    public static partial void LogReceivedResponse(this ILogger logger, AppMessage message);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unknown broadcast received: {Broadcast}")]
    public static partial void LogUnknownBroadcast(this ILogger logger, AppBroadcast broadcast);

    [LoggerMessage(Level = LogLevel.Error, Message = "Exception occurred on ConnectAsync.")]
    public static partial void LogConnectFailed(this ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Previous receive loop faulted before reconnect (expected).")]
    public static partial void LogPreviousReceiveLoopFaulted(this ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Background loop faulted during teardown (expected).")]
    public static partial void LogTeardownLoopFaulted(this ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Send loop stopped due to a WebSocketException.")]
    public static partial void LogSendLoopFaulted(this ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Disconnected from the Rust+ socket due to a WebSocketException.")]
    public static partial void LogReceiveWebSocketFault(this ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Disconnected from the Rust+ socket due to an Exception.")]
    public static partial void LogReceiveFault(this ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Broadcast-reply matcher threw; treating as no match.")]
    public static partial void LogMatcherThrew(this ILogger logger, Exception exception);
}
```

- [ ] **Step 5: Add the logger field and replace `Debug.WriteLine` in `RustPlusSocket.cs`**

In `src/RustPlusApi/RustPlusSocket.cs`:

1. Add `using Microsoft.Extensions.Logging;` and `using Microsoft.Extensions.Logging.Abstractions;` to the using block. Remove `using System.Diagnostics;` (it becomes unused once all `Debug.WriteLine` are gone).
2. Add the logger field near `_options` (after line ~33):

```csharp
    /// <summary>The client's logger; <c>NullLogger</c> when no factory was supplied. Categorised by
    /// the concrete runtime type so subclasses log under their own name.</summary>
    private readonly ILogger _logger =
        (options?.LoggerFactory ?? NullLoggerFactory.Instance).CreateLogger("RustPlusApi.RustPlusSocket");
```

   Note: a field initializer cannot call the instance method `GetType()`, so the category is the fixed string `"RustPlusApi.RustPlusSocket"`. (If a per-subclass category is later wanted, move resolution into a constructor body.)

1. Replace each `Debug.WriteLine(...)` with the matching log call:

| Old (`Debug.WriteLine`) | New |
| --- | --- |
| `$"Exception occured on ConnectAsync: {ex}"` | `_logger.LogConnectFailed(ex);` |
| `$"Previous receive loop faulted before reconnect (expected): {ex}"` | `_logger.LogPreviousReceiveLoopFaulted(ex);` |
| `$"Background loop faulted during teardown (expected): {ex}"` | `_logger.LogTeardownLoopFaulted(ex);` |
| `$"Send loop stopped due to a WebSocketException: {ex}"` | `_logger.LogSendLoopFaulted(ex);` |
| `"Receiving data from the Rust+ server..."` | `_logger.LogReceivingData();` |
| `"Waiting for data..."` | `_logger.LogWaitingForData();` |
| `$"Disconnected from the Rust+ socket due to a WebSocketException: {ex}"` | `_logger.LogReceiveWebSocketFault(ex);` |
| `$"Disconnected from the Rust+ socket due to an Exception: {ex}"` | `_logger.LogReceiveFault(ex);` |
| `"Receive loop exited."` | `_logger.LogReceiveLoopExited();` |
| `$"Received message:\n{message}"` | `_logger.LogReceivedMessage(message);` |
| `$"Received notification:\n{message}"` | `_logger.LogReceivedNotification(message);` |
| `$"Received response:\n{message}"` | `_logger.LogReceivedResponse(message);` |
| `$"Broadcast-reply matcher threw; treating as no match: {ex}"` | `_logger.LogMatcherThrew(ex);` |

   Note: `SafeMatches` is `static`; it cannot use the instance `_logger`. Make `SafeMatches` an instance method (remove `static`) so it can call `_logger.LogMatcherThrew(ex)`. Its only caller is `ResolveBroadcastReply` (instance), so this is safe.

- [ ] **Step 6: Replace `Debug.WriteLine` in `RustPlus.cs`**

In `src/RustPlusApi/RustPlus.cs`:

1. Remove `using System.Diagnostics;`.
2. `ParseNotification` cannot see `RustPlusSocket._logger` (it is `private`). Add a `protected` accessor on `RustPlusSocket` so derived classes log through the same logger. In `RustPlusSocket.cs`, just below the `_logger` field add:

```csharp
    /// <summary>The client logger, exposed to derived classes (e.g. <see cref="RustPlus"/>) so they
    /// log through the same categorised sink.</summary>
    private protected ILogger Logger => _logger;
```

1. Replace `Debug.WriteLine($"Unknown broadcast:\n{broadcast}");` with `Logger.LogUnknownBroadcast(broadcast);`.

- [ ] **Step 7: Run the logging tests — they should pass**

Run: `dotnet test tests/RustPlusApi.UnitTests/RustPlusApi.UnitTests.csproj --filter RustPlusLoggingTests`
Expected: PASS (all four tests).

- [ ] **Step 8: Build the whole solution to confirm no unused-using / analyzer breaks**

Run: `dotnet build`
Expected: Build succeeded, 0 warnings about unused `System.Diagnostics`.

- [ ] **Step 9: Commit**

```bash
git add src/RustPlusApi tests/RustPlusApi.UnitTests/SpyLogger.cs tests/RustPlusApi.UnitTests/RustPlusLoggingTests.cs
git commit -m "feat: add ILogger-based logging to the core Rust+ socket"
```

---

## Task 5: FCM logging — options, logger field, log methods, replace Debug.WriteLine

**Files:**

- Modify: `src/RustPlusApi.Fcm/RustPlusFcmSocketOptions.cs`
- Create: `src/RustPlusApi.Fcm/RustPlusFcmSocketLog.cs`
- Modify: `src/RustPlusApi.Fcm/RustPlusFcmSocket.cs`
- Modify: `src/RustPlusApi.Fcm/RustPlusFcm.cs`
- Create: `tests/RustPlusApi.Fcm.UnitTests/SpyLogger.cs`
- Create: `tests/RustPlusApi.Fcm.UnitTests/FcmLoggingTests.cs`

- [ ] **Step 1: Write the failing FCM logging tests**

Create `tests/RustPlusApi.Fcm.UnitTests/SpyLogger.cs` (identical shape to the core spy, in the FCM test namespace):

```csharp
using Microsoft.Extensions.Logging;

namespace RustPlusApi.Fcm.UnitTests;

public sealed class SpyLogger : ILogger
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
        => Entries.Add((logLevel, formatter(state, exception)));

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}

public sealed class SpyLoggerFactory : ILoggerFactory
{
    public SpyLogger Logger { get; } = new();
    public ILogger CreateLogger(string categoryName) => Logger;
    public void AddProvider(ILoggerProvider provider) { }
    public void Dispose() { }
}
```

Create `tests/RustPlusApi.Fcm.UnitTests/FcmLoggingTests.cs`:

```csharp
using Microsoft.Extensions.Logging;
using RustPlusApi.Fcm.Data;

namespace RustPlusApi.Fcm.UnitTests;

public class FcmLoggingTests
{
    private static Credentials AnyCredentials() =>
        new() { Gcm = new Gcm { AndroidId = 1, SecurityToken = 1 } };

    [Fact]
    public void Constructor_WithNoOptions_DoesNotThrow()
    {
        using var socket = new TestSocket(AnyCredentials(), null);
        Assert.NotNull(socket);
    }

    [Fact]
    public void Constructor_WithOptionsButNoFactory_DoesNotThrow()
    {
        using var socket = new TestSocket(AnyCredentials(), new RustPlusFcmSocketOptions());
        Assert.NotNull(socket);
    }

    [Fact]
    public void Constructor_WithLoggerFactory_DoesNotThrow()
    {
        var factory = new SpyLoggerFactory();
        using var socket = new TestSocket(AnyCredentials(),
            new RustPlusFcmSocketOptions { LoggerFactory = factory });
        Assert.NotNull(socket);
    }

    /// <summary>Concrete subclass: <see cref="RustPlusFcmSocket"/> is abstract.</summary>
    private sealed class TestSocket(Credentials credentials, RustPlusFcmSocketOptions? options)
        : RustPlusFcmSocket(credentials, options: options);
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusApi.Fcm.UnitTests/RustPlusApi.Fcm.UnitTests.csproj --filter FcmLoggingTests`
Expected: FAIL — `RustPlusFcmSocketOptions` has no `LoggerFactory` (compile error).

- [ ] **Step 3: Add `LoggerFactory` to `RustPlusFcmSocketOptions`**

In `src/RustPlusApi.Fcm/RustPlusFcmSocketOptions.cs`, add `using Microsoft.Extensions.Logging;` and inside the class:

```csharp
    /// <summary>Factory used to create the client's logger. When <see langword="null"/>, logging is
    /// disabled (a no-op <c>NullLogger</c> is used). Supply one to route diagnostics into your
    /// logging stack.</summary>
    public ILoggerFactory? LoggerFactory { get; init; }
```

- [ ] **Step 4: Add the FCM log methods**

Create `src/RustPlusApi.Fcm/RustPlusFcmSocketLog.cs`:

```csharp
using McsProto;
using Microsoft.Extensions.Logging;

namespace RustPlusApi.Fcm;

/// <summary>Source-generated, structured log messages for <see cref="RustPlusFcmSocket"/> and
/// <see cref="RustPlusFcm"/>. Generated bodies carry <c>[GeneratedCode]</c> and are excluded from
/// the coverage gate automatically.</summary>
internal static partial class RustPlusFcmSocketLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Exception occurred on ConnectAsync.")]
    public static partial void LogConnectFailed(this ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Background loop faulted during teardown (expected).")]
    public static partial void LogTeardownLoopFaulted(this ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Responding to ping: StreamId={StreamId}, Last={Last}, Status={Status}")]
    public static partial void LogRespondingToPing(this ILogger logger, int? streamId, int? last, long? status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignoring unrecognized tag: {Tag}")]
    public static partial void LogUnrecognizedTag(this ILogger logger, McsProtoTag tag);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No AppData found in message.")]
    public static partial void LogNoAppData(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Not a Rust+ notification - missing channelId or body.")]
    public static partial void LogNotRustNotification(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unknown channel: {ChannelId}")]
    public static partial void LogUnknownChannel(this ILogger logger, string channelId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unknown pairing type: {Type}")]
    public static partial void LogUnknownPairingType(this ILogger logger, string type);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unknown entity type: {EntityType}")]
    public static partial void LogUnknownEntityType(this ILogger logger, int entityType);
}
```

Note: confirm the `HeartbeatPing.Status` CLR type when wiring `LogRespondingToPing` (it is used as `ping.Status`); if it is not `long?`, change the parameter type to match. `McsProtoTag` is in the `RustPlusApi.Fcm.Data.Tags` types — adjust the `using` if the enum lives in a different namespace (it is referenced as `McsProtoTag` in `RustPlusFcmSocket.cs`).

- [ ] **Step 5: Add the logger field and replace `Debug.WriteLine` in `RustPlusFcmSocket.cs`**

In `src/RustPlusApi.Fcm/RustPlusFcmSocket.cs`:

1. Add `using Microsoft.Extensions.Logging;` and `using Microsoft.Extensions.Logging.Abstractions;`. Remove `using System.Diagnostics;`.
2. Add the logger field next to `_options` (after line ~38):

```csharp
    /// <summary>The client's logger; <c>NullLogger</c> when no factory was supplied.</summary>
    private readonly ILogger _logger =
        (options?.LoggerFactory ?? NullLoggerFactory.Instance).CreateLogger("RustPlusApi.Fcm.RustPlusFcmSocket");

    /// <summary>The client logger, exposed to derived classes (e.g. <see cref="RustPlusFcm"/>).</summary>
    private protected ILogger Logger => _logger;
```

1. Replace each `Debug.WriteLine(...)`:

| Old | New |
| --- | --- |
| `$"Exception occured on ConnectAsync: {ex}"` | `_logger.LogConnectFailed(ex);` |
| `$"Background loop faulted during teardown (expected): {ex}"` | `_logger.LogTeardownLoopFaulted(ex);` |
| `$"Responding to ping: Stream ID: {ping.StreamId},Last: {ping.LastStreamIdReceived},Status: {ping.Status}"` (the multi-line interpolated string) | `_logger.LogRespondingToPing(ping.StreamId, ping.LastStreamIdReceived, ping.Status);` |
| `$"Ignoring unrecognized tag: {e.Tag}"` | `_logger.LogUnrecognizedTag(e.Tag);` |
| `"⚠️ No AppData found in message"` | `_logger.LogNoAppData();` |
| `"⚠️ Not a Rust+ notification - missing channelId or body"` | `_logger.LogNotRustNotification();` |

- [ ] **Step 6: Replace `Debug.WriteLine` in `RustPlusFcm.cs`**

In `src/RustPlusApi.Fcm/RustPlusFcm.cs`:

1. Remove `using System.Diagnostics;`.
2. Replace:
   - `Debug.WriteLine($"Unknown channel: {message.Data.ChannelId}");` → `Logger.LogUnknownChannel(message.Data.ChannelId);`
   - `Debug.WriteLine($"Unknown pairing type: {body.Type}");` → `Logger.LogUnknownPairingType(body.Type);`
   - `Debug.WriteLine($"Unknown entity type: {body.EntityType}");` → `Logger.LogUnknownEntityType(body.EntityType);`

- [ ] **Step 7: Run the FCM logging tests**

Run: `dotnet test tests/RustPlusApi.Fcm.UnitTests/RustPlusApi.Fcm.UnitTests.csproj --filter FcmLoggingTests`
Expected: PASS.

- [ ] **Step 8: Run the full FCM unit suite to confirm no regression**

Run: `dotnet test tests/RustPlusApi.Fcm.UnitTests/RustPlusApi.Fcm.UnitTests.csproj`
Expected: All pass (framing/lifecycle/teardown tests still green).

- [ ] **Step 9: Build the whole solution**

Run: `dotnet build`
Expected: Build succeeded, 0 errors, no unused-using warnings.

- [ ] **Step 10: Commit**

```bash
git add src/RustPlusApi.Fcm tests/RustPlusApi.Fcm.UnitTests/SpyLogger.cs tests/RustPlusApi.Fcm.UnitTests/FcmLoggingTests.cs
git commit -m "feat: add ILogger-based logging to the FCM socket"
```

---

## Task 6: Update Stryker `ignore-methods`

**Files:**

- Modify: `tests/RustPlusApi.Fcm.UnitTests/stryker-config.json`
- Modify: `tests/RustPlusApi.Camera.UnitTests/stryker-config.json`
- Modify: `tests/RustPlusApi.Fcm.Registration.UnitTests/stryker-config.json`

- [ ] **Step 1: Replace the `Debug.WriteLine` ignore with logging ignores**

In each of the three `stryker-config.json` files, change:

```json
    "ignore-methods": ["ConfigureAwait", "Debug.WriteLine", "SuppressFinalize"],
```

to:

```json
    "ignore-methods": ["ConfigureAwait", "Log*", "CreateLogger", "SuppressFinalize"],
```

`Log*` matches every `[LoggerMessage]` method (they are `Log`-prefixed) plus any direct `ILogger.LogX` extension call; `CreateLogger` keeps the logger-resolution call un-mutated.

- [ ] **Step 2: Validate the JSON parses**

Run: `python3 -c "import json,glob; [json.load(open(f)) for f in glob.glob('tests/**/stryker-config.json', recursive=True)]; print('ok')"`
Expected: `ok`

- [ ] **Step 3: (Optional, slow) Run FCM mutation testing locally if tooling is available**

Run: `cd tests/RustPlusApi.Fcm.UnitTests && dotnet stryker --config-file stryker-config.json --project RustPlusApi.Fcm.csproj --reporter cleartext ; cd ../..`
Expected: completes; mutation score at or above thresholds (`break: 75`). If `dotnet stryker` is not installed (`dotnet tool restore` first), skip — CI runs it weekly.

- [ ] **Step 4: Commit**

```bash
git add tests/RustPlusApi.Fcm.UnitTests/stryker-config.json tests/RustPlusApi.Camera.UnitTests/stryker-config.json tests/RustPlusApi.Fcm.Registration.UnitTests/stryker-config.json
git commit -m "test: ignore logging calls in Stryker mutation configs"
```

---

## Task 7: Verify the coverage gate and document exclusions

**Files:**

- Modify: `docs/development/testing.md`

- [ ] **Step 1: Run the full coverage gate**

Run: `tools/coverage/report.sh`
Expected: the full suite runs, ReportGenerator merges per-project Cobertura, and `tools/coverage/check_threshold.py` reports line and branch coverage meeting the configured gate (the repo standard is 100% for non-excluded members). If the gate fails, the report names the uncovered lines — they will be new log **call sites** on a path no test reaches, or the `LoggerFactory` branch; add/extend a test to cover them (the generated log bodies and record members are excluded automatically and will not appear).

- [ ] **Step 2: Document the new exclusions**

In `docs/development/testing.md`, under "Coverage exclusion list", add a subsection:

```markdown
### `[LoggerMessage]` generated log methods and `RustPlusConnection` record members

**Files:** `src/RustPlusApi/RustPlusSocketLog.cs`, `src/RustPlusApi.Fcm/RustPlusFcmSocketLog.cs`,
`src/RustPlusApi/RustPlusConnection.cs`

**Justification:** The `[LoggerMessage]` source generator emits the partial log-method bodies with
`[GeneratedCode]`, and the C# compiler emits the record's `Equals`/`GetHashCode`/`ToString`/
`Deconstruct`/copy-constructor/equality members with `[CompilerGenerated]`. Both are already dropped
by the `ExcludeByAttribute` rule (`GeneratedCodeAttribute`, `CompilerGeneratedAttribute`) in the
`coverlet.runsettings` files, with positional property accessors handled by `SkipAutoProps`. No
bespoke tests are required for these members; the logging behaviour is exercised through the call
sites in `RustPlusLoggingTests` / `FcmLoggingTests`.
```

- [ ] **Step 3: Note the Stryker ignore-methods change**

In `docs/development/testing.md`, in the Stryker configuration section (around the thresholds list), add a sentence:

```markdown
Logging calls are excluded from mutation via `ignore-methods` (`Log*`, `CreateLogger`): the
`[LoggerMessage]`-generated methods are non-functional diagnostics, so mutating their arguments
would only produce equivalent or low-value mutants.
```

- [ ] **Step 4: Commit**

```bash
git add docs/development/testing.md
git commit -m "docs: record logging/record coverage exclusions and Stryker ignore change"
```

---

## Task 8: Update the package READMEs

**Files:**

- Modify: `src/RustPlusApi/README.md`
- Modify: `src/RustPlusApi.Fcm/README.md`

- [ ] **Step 1: Update the core README constructor example + add a logging snippet**

In `src/RustPlusApi/README.md`, find the `new RustPlus(` usage example and replace it with the record form, then add a short logging section. Use this content (adapt surrounding prose to match the file's existing style):

```markdown
```csharp
using var rustPlus = new RustPlus(new RustPlusConnection(
    server: "127.0.0.1",
    port: 28082,
    playerId: 76561198000000000,
    playerToken: 123456789));
```

### Logging

Pass an `ILoggerFactory` via the options to route the client's diagnostics into your logging stack:

```csharp
using Microsoft.Extensions.Logging;

using var loggerFactory = LoggerFactory.Create(b => b.AddConsole());

using var rustPlus = new RustPlus(
    new RustPlusConnection("127.0.0.1", 28082, 76561198000000000, 123456789),
    new RustPlusSocketOptions { LoggerFactory = loggerFactory });
```

```

- [ ] **Step 2: Add a logging snippet to the FCM README**

In `src/RustPlusApi.Fcm/README.md`, add a "Logging" section near the usage example:

```markdown
### Logging

```csharp
using Microsoft.Extensions.Logging;

using var loggerFactory = LoggerFactory.Create(b => b.AddConsole());

var fcm = new RustPlusFcm(
    credentials,
    options: new RustPlusFcmSocketOptions { LoggerFactory = loggerFactory });
```

```

- [ ] **Step 3: Commit**

```bash
git add src/RustPlusApi/README.md src/RustPlusApi.Fcm/README.md
git commit -m "docs: update READMEs for RustPlusConnection and ILogger support"
```

---

## Task 9: Update the DocFX site

**Files:**

- Modify: DocFX articles under `docs/articles/` that show a `new RustPlus(` (client) example: `getting-started.md`, `rustplus-client.md`, `recipes.md`, `samples.md`, `troubleshooting.md`, and `docs/index.md`.
- Create or modify: a Logging article/section + `docs/toc.yml` (TOC) if a new file is added.

- [ ] **Step 1: Inventory the client constructor examples**

Run: `grep -rn 'new RustPlus(' docs/articles docs/index.md | grep -v 'new RustPlusFcm' | grep -v 'new RustPlusConnection'`
Expected: a list of `new RustPlus(server, port, playerId, playerToken[, …])` examples to update. (`new RustPlusFcm(...)` hits in `fcm-notifications.md`/`credentials.md` are unchanged in shape — leave them unless adding a logging snippet.)

- [ ] **Step 2: Update each client example to the record form**

For every hit from Step 1, wrap the connection arguments in `new RustPlusConnection(...)`, keeping any `options` argument as the second `RustPlus` argument. Example transformation:

```csharp
// before
var rustPlus = new RustPlus("127.0.0.1", 28082, playerId, playerToken);
// after
var rustPlus = new RustPlus(new RustPlusConnection("127.0.0.1", 28082, playerId, playerToken));
```

- [ ] **Step 3: Add a Logging section**

Add a "Logging" section to `docs/articles/getting-started.md` (or a new `docs/articles/logging.md` registered in `docs/toc.yml`). Content:

```markdown
## Logging

Both clients are silent by default. Supply an `ILoggerFactory` through the options object to receive
structured diagnostics (connect/receive/teardown lifecycle, dropped frames, unknown messages, errors):

```csharp
using Microsoft.Extensions.Logging;

using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

using var rustPlus = new RustPlus(
    new RustPlusConnection("127.0.0.1", 28082, playerId, playerToken),
    new RustPlusSocketOptions { LoggerFactory = loggerFactory });
```

The FCM client accepts the same `LoggerFactory` on `RustPlusFcmSocketOptions`. When no factory is
supplied, logging is a no-op (`NullLogger`) with zero overhead.

```

If you created `docs/articles/logging.md`, add it to `docs/toc.yml` under the Articles node, e.g.:

```yaml
  - name: Logging
    href: articles/logging.md
```

- [ ] **Step 4: Build the DocFX site to verify it renders**

Follow `docs/development/building-docs.md`. Typically:

Run: `docfx docs/docfx.json` (or the command documented in `building-docs.md`)
Expected: build completes with no broken-link / missing-file errors; do **not** hand-edit `docs/_site/`.

- [ ] **Step 5: Commit**

```bash
git add docs/articles docs/index.md docs/toc.yml docs/_site
git commit -m "docs: update DocFX site for RustPlusConnection and logging"
```

---

## Task 10: Final full verification

**Files:** none (verification only)

- [ ] **Step 1: Clean build of the whole solution, both TFMs**

Run: `dotnet build -c Release`
Expected: Build succeeded, 0 errors, 0 warnings.

- [ ] **Step 2: Run the entire test suite**

Run: `dotnet test`
Expected: All tests pass across the TFM matrix.

- [ ] **Step 3: Run the coverage gate**

Run: `tools/coverage/report.sh`
Expected: line and branch coverage meet the gate.

- [ ] **Step 4: Build the .NET Framework smoke project**

Run: `dotnet build tests/RustPlusApi.NetFrameworkSmoke/RustPlusApi.NetFrameworkSmoke.csproj`
Expected: Build succeeded — proves the new public API (record constructor) is reachable from a `net48`/`netstandard2.0` consumer.

- [ ] **Step 5: Confirm no `Debug.WriteLine` remains in the two libraries**

Run: `grep -rn 'Debug.WriteLine' src/RustPlusApi src/RustPlusApi.Fcm`
Expected: no output.

- [ ] **Step 6: Confirm no orphaned `<param>` tags reference removed constructor parameters**

Run: `grep -rn 'param name="server"\|param name="playerId"\|param name="playerToken"\|param name="useFacepunchProxy"' src/RustPlusApi/RustPlus.cs src/RustPlusApi/RustPlusSocket.cs`
Expected: no output (those `<param>` docs now live on `RustPlusConnection`).

- [ ] **Step 7: Final commit (if any verification fixes were made)**

```bash
git add -A
git commit -m "chore: final verification fixes for logging + constructor change"
```

---

## Self-review notes

- **Spec coverage:** logging dependency (T1), `RustPlusConnection` + doc migration (T2, T3, T10 step 6), `LoggerFactory` on both options (T4, T5), `NullLogger` default + both branch sides (T4 steps 1–7, covered by the three constructor tests), `[LoggerMessage]` replacement of every `Debug.WriteLine` (T4, T5, verified T10 step 5), FCM constructor unchanged (untouched), tests (T4, T5), coverage gate + exclusion docs (T7), Stryker ignore-methods (T6), READMEs (T8), DocFX + TOC (T9). All spec sections map to a task.
- **Open assumptions flagged in-task:** `HeartbeatPing.Status` CLR type (T5 step 4) and `McsProtoTag` namespace — verify against the generated MCS contracts when wiring the ping log method. The category strings use fixed names because `GetType()` is unavailable in a field initializer (documented in T4 step 5 / T5 step 5).
