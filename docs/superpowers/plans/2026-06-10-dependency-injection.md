# Dependency-Injection Support Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reshape the unreleased logging surface to constructor injection (removing `options.LoggerFactory` and `SetPlayer`), then ship two DI extension packages (`RustPlusApi.Extensions.DependencyInjection`, `RustPlusApi.Fcm.Extensions.DependencyInjection`) with factory + single-client registration.

**Architecture:** The socket clients gain an optional `ILoggerFactory?` constructor parameter; the options classes become pure config-bindable tuning. Each DI package wraps the existing constructors: an `IRustPlusFactory`/`IRustPlusFcmFactory` singleton for runtime connections (caller-owned clients), and `AddRustPlus`/`AddRustPlusFcm` overloads registering one container-owned singleton client. Logging auto-wires from the host's `ILoggerFactory`; tuning flows through `IOptions<T>`.

**Tech Stack:** C# multi-target `netstandard2.0; net10.0`, Microsoft.Extensions.{DependencyInjection.Abstractions, Options, Configuration.Binder} 10.0.x, xUnit, coverlet + ReportGenerator gate, Stryker, DocFX, Central Package Management.

**Spec:** `docs/superpowers/specs/2026-06-10-dependency-injection-design.md`

**⚠ Spec deviations (discovered during planning, required for the design to function):**

1. **Options properties become `get; set;` (were `init`).** `services.Configure<T>(Action<T>)` mutates the options instance after construction; `init`-only setters make `o.RequestTimeout = …` a compile error inside the configure lambda. Standard M.E.Options POCOs are fully settable. The "never mutated by the client" guarantee stays true; the doc comments are updated to say "treat as fixed after construction".
2. **Package choice:** the spec listed `Microsoft.Extensions.Options.ConfigurationExtensions`; the API actually used is `IConfiguration.Get<T>()`, which lives in `Microsoft.Extensions.Configuration.Binder` (a dependency of the former). The plan references `Configuration.Binder` directly.

**Conventions in this repo:**

- `ImplicitUsings` is **enabled** repo-wide; explicit `using System;` is tolerated but unnecessary. `TreatWarningsAsErrors=true` with `latest-all` analyzers (NetAnalyzers, Roslynator, Sonar) — expect RCS1141 (XML `<param>` required on primary-constructor classes), CA1307/CA1305 (explicit `StringComparison`/culture), and null-guard rules on public APIs. When the build flags a using/doc issue, fix it as directed by the diagnostic.
- Commits only at explicit commit steps. The executor appends the standard `Co-Authored-By` footer.
- `dotnet build` / `dotnet test` at the repo root operate on `RustPlusApi.sln` (both TFMs).

---

## Branch setup

This work modifies code that lives on PR #61's branch (`feat/logging-and-connection`): the logger field, options properties, and logging tests it introduced.

- If PR #61 is **not yet merged**: `git switch feat/logging-and-connection && git pull && git switch -c feat/dependency-injection` (stacked branch; retarget its PR after #61 merges).
- If PR #61 **is merged**: `git switch develop && git pull && git switch -c feat/dependency-injection`.

---

## File Structure

**Created:**

- `src/RustPlusApi.Extensions.DependencyInjection/RustPlusApi.Extensions.DependencyInjection.csproj`
- `src/RustPlusApi.Extensions.DependencyInjection/IRustPlusFactory.cs` — public factory contract.
- `src/RustPlusApi.Extensions.DependencyInjection/RustPlusFactory.cs` — internal default factory.
- `src/RustPlusApi.Extensions.DependencyInjection/RustPlusServiceCollectionExtensions.cs` — `AddRustPlusFactory` + 3 `AddRustPlus` overloads.
- `src/RustPlusApi.Extensions.DependencyInjection/README.md`
- `src/RustPlusApi.Fcm.Extensions.DependencyInjection/RustPlusApi.Fcm.Extensions.DependencyInjection.csproj`
- `src/RustPlusApi.Fcm.Extensions.DependencyInjection/IRustPlusFcmFactory.cs`
- `src/RustPlusApi.Fcm.Extensions.DependencyInjection/RustPlusFcmFactory.cs`
- `src/RustPlusApi.Fcm.Extensions.DependencyInjection/RustPlusFcmServiceCollectionExtensions.cs`
- `src/RustPlusApi.Fcm.Extensions.DependencyInjection/README.md`
- `tests/RustPlusApi.Extensions.DependencyInjection.UnitTests/` — csproj, `RecordingLoggerFactory.cs`, `RustPlusFactoryTests.cs`, `RustPlusServiceCollectionExtensionsTests.cs`, `coverlet.runsettings`, `stryker-config.json`
- `tests/RustPlusApi.Fcm.Extensions.DependencyInjection.UnitTests/` — csproj, `RecordingLoggerFactory.cs`, `RustPlusFcmServiceCollectionExtensionsTests.cs`, `coverlet.runsettings`, `stryker-config.json`
- `docs/articles/dependency-injection.md`

**Modified:**

- `Directory.Packages.props` — five new `PackageVersion` pins.
- `src/RustPlusApi/RustPlusSocketOptions.cs`, `src/RustPlusApi.Fcm/RustPlusFcmSocketOptions.cs` — drop `LoggerFactory`, `init`→`set`.
- `src/RustPlusApi/RustPlusSocket.cs` — ctor param, logger field source, remove `SetPlayer`, tighten `PlayerId`/`_playerToken`.
- `src/RustPlusApi/RustPlus.cs`, `src/RustPlusApi.Fcm/RustPlusFcmSocket.cs`, `src/RustPlusApi.Fcm/RustPlusFcm.cs` — ctor param forwarding.
- `tests/RustPlusApi.UnitTests/RustPlusLoggingTests.cs`, `tests/RustPlusApi.Fcm.UnitTests/FcmLoggingTests.cs` — new ctor shape.
- `tests/RustPlusApi.IntegrationTests/SocketLifecycleTests.cs` — remove `SetPlayer` test.
- `.github/workflows/Mutation.yml`, `docs/development/testing.md` — new mutation projects.
- `src/RustPlusApi/README.md`, `src/RustPlusApi.Fcm/README.md`, `docs/articles/logging.md`, `docs/articles/toc.yml`, root `README.md`.
- `RustPlusApi.sln` — four new projects.

---

## Task 1: Branch + package pins

**Files:**

- Modify: `Directory.Packages.props`

- [ ] **Step 1: Create the working branch**

Follow the "Branch setup" section above (stacked on `feat/logging-and-connection`, or on `develop` if #61 merged). Verify: `git status -sb` shows `## feat/dependency-injection` and a clean tree.

- [ ] **Step 2: Pin the new packages (CPM)**

In `Directory.Packages.props`, add inside the first `<ItemGroup>` (runtime packages, next to `Microsoft.Extensions.Logging.Abstractions`):

```xml
<PackageVersion Include="Microsoft.Extensions.Configuration.Binder" Version="10.0.8" />
<PackageVersion Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.8" />
<PackageVersion Include="Microsoft.Extensions.Options" Version="10.0.8" />
```

And in the test-stack `<ItemGroup>` (next to `xunit`):

```xml
<PackageVersion Include="Microsoft.Extensions.Configuration" Version="10.0.8" />
<PackageVersion Include="Microsoft.Extensions.DependencyInjection" Version="10.0.8" />
```

(If `dotnet restore` later reports a missing 10.0.8 for any of these, use the nearest existing 10.0.x and keep all five on the same patch.)

- [ ] **Step 3: Commit**

```bash
git add Directory.Packages.props
git commit -m "build: pin DI, Options and Configuration.Binder packages"
```

---

## Task 2: Core reshape — constructor-injected logger, settable options, remove `SetPlayer`

**Files:**

- Modify: `src/RustPlusApi/RustPlusSocketOptions.cs`
- Modify: `src/RustPlusApi/RustPlusSocket.cs`
- Modify: `src/RustPlusApi/RustPlus.cs`
- Modify: `tests/RustPlusApi.UnitTests/RustPlusLoggingTests.cs`
- Modify: `tests/RustPlusApi.IntegrationTests/SocketLifecycleTests.cs`

- [ ] **Step 1: Update the failing tests first (TDD on the new shape)**

In `tests/RustPlusApi.UnitTests/RustPlusLoggingTests.cs`, replace the two factory-using tests and the nested class:

```csharp
    [Fact]
    public void Constructor_WithLoggerFactory_DoesNotThrow()
    {
        var factory = new SpyLoggerFactory();
        using var client = new RustPlus(AnyConnection(), loggerFactory: factory);
        Assert.False(client.IsConnected);
    }

    [Fact]
    public void UnknownBroadcast_LogsWarning()
    {
        var factory = new SpyLoggerFactory();
        using var client = new TestableRustPlus(factory);

        client.InvokeParseNotification(new AppBroadcast());

        Assert.Single(factory.Logger.Entries, e =>
            e.Level == LogLevel.Warning && e.Message.Contains("Unknown broadcast", StringComparison.Ordinal));
    }

    /// <summary>Exposes the protected ParseNotification so the unknown-broadcast path can be driven.</summary>
    /// <param name="loggerFactory">The logger factory under test.</param>
    private sealed class TestableRustPlus(ILoggerFactory loggerFactory)
        : RustPlus(new RustPlusConnection("127.0.0.1", 1, 1UL, 1), loggerFactory: loggerFactory)
    {
        public void InvokeParseNotification(AppBroadcast broadcast) => ParseNotification(broadcast);
    }
```

Keep `Constructor_WithNoOptions_DoesNotThrow` and `Constructor_WithOptionsButNoFactory_DoesNotThrow` unchanged (they still cover the `loggerFactory == null` branch).

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusApi.UnitTests/RustPlusApi.UnitTests.csproj --filter RustPlusLoggingTests`
Expected: FAIL — compile error, `RustPlus` has no `loggerFactory` parameter.

- [ ] **Step 3: Reshape `RustPlusSocketOptions`**

In `src/RustPlusApi/RustPlusSocketOptions.cs`: delete the `LoggerFactory` property and its doc comment, delete `using Microsoft.Extensions.Logging;`, change every remaining property from `{ get; init; }` to `{ get; set; }`, and update the class doc's last sentence from "Properties are init-only: configure the instance at construction, then share it freely — it is never mutated by the client." to:

```
/// instance at construction and treat it as fixed afterwards — the client never mutates it.
```

(Adjust the preceding sentence fragment so the summary reads naturally; the four tuning properties and their docs are otherwise unchanged.)

- [ ] **Step 4: Reshape the `RustPlusSocket` constructor and logger field**

In `src/RustPlusApi/RustPlusSocket.cs`:

1. Class header gains the parameter:

```csharp
public abstract class RustPlusSocket(
    RustPlusConnection connection,
    RustPlusSocketOptions? options = null,
    ILoggerFactory? loggerFactory = null)
    : IRustPlusSocket, IDisposable, IAsyncDisposable
```

2. Add to the class-level XML doc block (after the `options` param):

```csharp
/// <param name="loggerFactory">Routes the client's diagnostics into your logging stack; logging is
/// disabled (a no-op <c>NullLogger</c>) when <see langword="null"/>.</param>
```

3. The logger field changes source (same doc comment):

```csharp
    private protected readonly ILogger Logger =
        (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger("RustPlusApi.RustPlusSocket");
```

- [ ] **Step 5: Remove `SetPlayer` and tighten identity fields**

Still in `src/RustPlusApi/RustPlusSocket.cs`:

1. Delete the whole `SetPlayer` method including its XML doc (the block starting `/// Sets the player ID and player token…` through the closing brace of `public void SetPlayer(ulong newPlayerId, int newPlayerToken)`).
2. `private int _playerToken = connection.PlayerToken;` → `private readonly int _playerToken = connection.PlayerToken;`
3. Replace

```csharp
    /// <summary>The Steam ID requests are currently issued as (see <see cref="SetPlayer"/>).</summary>
    protected ulong PlayerId { get; private set; } = connection.PlayerId;
```

with

```csharp
    /// <summary>The Steam ID requests are issued as.</summary>
    protected ulong PlayerId { get; } = connection.PlayerId;
```

- [ ] **Step 6: Forward the parameter in `RustPlus`**

In `src/RustPlusApi/RustPlus.cs`, class header:

```csharp
public class RustPlus(RustPlusConnection connection, RustPlusSocketOptions? options = null, ILoggerFactory? loggerFactory = null)
    : RustPlusSocket(connection, options, loggerFactory), IRustPlus
```

Add `using Microsoft.Extensions.Logging;` and the same `<param name="loggerFactory">` doc line to its class doc block.

- [ ] **Step 7: Remove the `SetPlayer` integration test**

In `tests/RustPlusApi.IntegrationTests/SocketLifecycleTests.cs`: delete the entire `SetPlayer_ChangesCredentialsOnNextRequest` test method, and change the class doc

```csharp
/// Covers socket lifecycle events, SetPlayer, disconnect variants, and the
```

to

```csharp
/// Covers socket lifecycle events, disconnect variants, and the
```

- [ ] **Step 8: Build and run the core suites**

Run: `dotnet build && dotnet test tests/RustPlusApi.UnitTests/RustPlusApi.UnitTests.csproj && dotnet test tests/RustPlusApi.IntegrationTests/RustPlusApi.IntegrationTests.csproj`
Expected: build clean (0 warnings/errors); all tests pass, including the updated `RustPlusLoggingTests`. (The FCM projects still compile — they were untouched so far.)

- [ ] **Step 9: Verify nothing references the removed surface**

Run: `grep -rn "SetPlayer\b\|LoggerFactory = " src/RustPlusApi tests/RustPlusApi.UnitTests tests/RustPlusApi.IntegrationTests --include='*.cs' | grep -v "CanSetPlayerNotes" | grep -v "loggerFactory"`
Expected: no output.

- [ ] **Step 10: Commit**

```bash
git add src/RustPlusApi tests/RustPlusApi.UnitTests tests/RustPlusApi.IntegrationTests
git commit -m "refactor!: constructor-injected ILoggerFactory, settable options, remove SetPlayer"
```

---

## Task 3: FCM reshape — constructor-injected logger, settable options

**Files:**

- Modify: `src/RustPlusApi.Fcm/RustPlusFcmSocketOptions.cs`
- Modify: `src/RustPlusApi.Fcm/RustPlusFcmSocket.cs`
- Modify: `src/RustPlusApi.Fcm/RustPlusFcm.cs`
- Modify: `tests/RustPlusApi.Fcm.UnitTests/FcmLoggingTests.cs`

- [ ] **Step 1: Update the failing tests first**

In `tests/RustPlusApi.Fcm.UnitTests/FcmLoggingTests.cs`, replace the factory-using test, the unknown-channel test's construction, and the two nested classes:

```csharp
    [Fact]
    public void Constructor_WithLoggerFactory_DoesNotThrow()
    {
        var factory = new SpyLoggerFactory();
        using var socket = new TestSocket(AnyCredentials(), loggerFactory: factory);
        Assert.NotNull(socket);
    }

    [Fact]
    public void ParseNotification_UnknownChannel_LogsWarning()
    {
        var factory = new SpyLoggerFactory();
        using var fcm = new TestableRustPlusFcm(factory);

        fcm.InvokeParseNotification(new FcmMessage
        {
            Data = new MessageData { ChannelId = "not-a-known-channel" }
        });

        Assert.Single(factory.Logger.Entries, e =>
            e.Level == LogLevel.Warning && e.Message.Contains("Unknown channel", StringComparison.Ordinal));
    }

    /// <summary>Concrete subclass: <see cref="RustPlusFcmSocket"/> is abstract.</summary>
    /// <param name="credentials">The FCM credentials.</param>
    /// <param name="options">Optional socket options.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    private sealed class TestSocket(Credentials credentials, RustPlusFcmSocketOptions? options = null, ILoggerFactory? loggerFactory = null)
        : RustPlusFcmSocket(credentials, options: options, loggerFactory: loggerFactory);

    /// <summary>Exposes the protected ParseNotification so the unknown-channel path can be driven.</summary>
    /// <param name="loggerFactory">The logger factory under test.</param>
    private sealed class TestableRustPlusFcm(ILoggerFactory loggerFactory)
        : RustPlusFcm(new Credentials { Gcm = new Gcm { AndroidId = 1, SecurityToken = 1 } }, loggerFactory: loggerFactory)
    {
        public void InvokeParseNotification(FcmMessage message) => ParseNotification(message);
    }
```

The existing `Constructor_WithNoOptions_DoesNotThrow` (`new TestSocket(AnyCredentials(), null)`) and `Constructor_WithOptionsButNoFactory_DoesNotThrow` keep compiling against the widened `TestSocket` and stay as the null-branch coverage.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusApi.Fcm.UnitTests/RustPlusApi.Fcm.UnitTests.csproj --filter FcmLoggingTests`
Expected: FAIL — `RustPlusFcmSocket` has no `loggerFactory` parameter.

- [ ] **Step 3: Reshape `RustPlusFcmSocketOptions`**

In `src/RustPlusApi.Fcm/RustPlusFcmSocketOptions.cs`: delete the `LoggerFactory` property + doc, delete `using Microsoft.Extensions.Logging;`, change `HeartbeatInterval` and `InactivityTimeout` to `{ get; set; }`, and update the class doc's "Properties are init-only: configure at construction, then share freely." to "Configure at construction and treat as fixed afterwards — the client never mutates it."

- [ ] **Step 4: Reshape the FCM socket + client constructors**

In `src/RustPlusApi.Fcm/RustPlusFcmSocket.cs`:

```csharp
public abstract class RustPlusFcmSocket(
    Credentials credentials,
    ICollection<string>? persistentIds = null,
    RustPlusFcmSocketOptions? options = null,
    ILoggerFactory? loggerFactory = null)
    : IRustPlusFcmSocket, IDisposable, IAsyncDisposable
```

with the doc line added after the `options` param:

```csharp
/// <param name="loggerFactory">Routes the client's diagnostics into your logging stack; logging is
/// disabled (a no-op <c>NullLogger</c>) when <see langword="null"/>.</param>
```

and the logger field:

```csharp
    private protected readonly ILogger Logger =
        (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger("RustPlusApi.Fcm.RustPlusFcmSocket");
```

In `src/RustPlusApi.Fcm/RustPlusFcm.cs`:

```csharp
public class RustPlusFcm(Credentials credentials, ICollection<string>? persistentIds = null, RustPlusFcmSocketOptions? options = null, ILoggerFactory? loggerFactory = null)
    : RustPlusFcmSocket(credentials, persistentIds, options, loggerFactory), IRustPlusFcm
```

with `using Microsoft.Extensions.Logging;` and the same doc line.

- [ ] **Step 5: Run the FCM suite**

Run: `dotnet build && dotnet test tests/RustPlusApi.Fcm.UnitTests/RustPlusApi.Fcm.UnitTests.csproj`
Expected: build clean; all FCM tests pass (framing/lifecycle/teardown subclasses don't pass options and keep compiling).

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusApi.Fcm tests/RustPlusApi.Fcm.UnitTests
git commit -m "refactor!: constructor-injected ILoggerFactory and settable options for FCM"
```

---

## Task 4: Reshape documentation ripple

**Files:**

- Modify: `src/RustPlusApi/README.md`
- Modify: `src/RustPlusApi.Fcm/README.md`
- Modify: `docs/articles/logging.md`

- [ ] **Step 1: Core README logging snippet**

In `src/RustPlusApi/README.md`, the "## Logging" section: change the lead line to "Pass an `ILoggerFactory` to the constructor to route the client's diagnostics into your logging stack:" and the snippet to:

```csharp
using Microsoft.Extensions.Logging;

using var loggerFactory = LoggerFactory.Create(b => b.AddConsole());

using var rustPlus = new RustPlus(
    new RustPlusConnection("127.0.0.1", 28082, 76561198000000000, 123456789),
    loggerFactory: loggerFactory);
```

- [ ] **Step 2: FCM README logging snippet**

In `src/RustPlusApi.Fcm/README.md`, "### Logging" snippet becomes:

```csharp
using Microsoft.Extensions.Logging;

using var loggerFactory = LoggerFactory.Create(b => b.AddConsole());

var fcm = new RustPlusFcm(credentials, loggerFactory: loggerFactory);
```

- [ ] **Step 3: DocFX logging article**

In `docs/articles/logging.md`: change "Supply an `ILoggerFactory` through the options object" to "Supply an `ILoggerFactory` to the constructor", update the snippet to `new RustPlus(new RustPlusConnection("127.0.0.1", 28082, playerId, playerToken), loggerFactory: loggerFactory)`, and the closing line to "The FCM client accepts the same `loggerFactory` constructor parameter. When none is supplied, logging is a no-op (`NullLogger`) with zero overhead."

- [ ] **Step 4: Verify no stale references and commit**

Run: `grep -rn "LoggerFactory = " src/*/README.md docs/articles/*.md`
Expected: no output.

```bash
git add src/RustPlusApi/README.md src/RustPlusApi.Fcm/README.md docs/articles/logging.md
git commit -m "docs: logging snippets use constructor-injected ILoggerFactory"
```

---

## Task 5: Core DI package + tests

**Files:**

- Create: `src/RustPlusApi.Extensions.DependencyInjection/` (csproj + 3 source files)
- Create: `tests/RustPlusApi.Extensions.DependencyInjection.UnitTests/` (csproj + 3 source files)
- Modify: `RustPlusApi.sln`

- [ ] **Step 1: Create the package project**

`src/RustPlusApi.Extensions.DependencyInjection/RustPlusApi.Extensions.DependencyInjection.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <TargetFrameworks>netstandard2.0; net10.0</TargetFrameworks>
        <PackageId>RustPlusApi.Extensions.DependencyInjection</PackageId>
        <IsPackable>true</IsPackable>
        <Authors>HandyS11</Authors>
        <Owners>HandyS11</Owners>
        <Product>Dependency-injection extensions for RustPlusApi.</Product>
        <Description>Microsoft.Extensions.DependencyInjection registration extensions for the RustPlusApi Rust+ client.</Description>
        <PackageTags>rust rustplus rustplusapi dependency-injection</PackageTags>
        <PackageLicenseExpression>MIT</PackageLicenseExpression>
        <LicenseUrl>https://github.com/HandyS11/RustPlusApi/blob/main/LICENSE</LicenseUrl>
        <PackageRequireLicenseAcceptance>true</PackageRequireLicenseAcceptance>
        <RepositoryUrl>https://github.com/HandyS11/RustPlusApi</RepositoryUrl>
        <RepositoryType>git</RepositoryType>
        <PackageReadmeFile>README.md</PackageReadmeFile>
        <PackageIcon>icon.png</PackageIcon>
        <IncludeSymbols>true</IncludeSymbols>
        <SymbolPackageFormat>snupkg</SymbolPackageFormat>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="Microsoft.Extensions.Configuration.Binder" />
        <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
        <PackageReference Include="Microsoft.Extensions.Options" />
    </ItemGroup>

    <ItemGroup>
        <ProjectReference Include="..\RustPlusApi\RustPlusApi.csproj" />
    </ItemGroup>

    <ItemGroup>
        <None Include="README.md" Pack="true" PackagePath="\" />
        <None Include="../../icon.png" Pack="true" PackagePath="\"/>
    </ItemGroup>

</Project>
```

Also create a placeholder `src/RustPlusApi.Extensions.DependencyInjection/README.md` containing just `# RustPlusApi.Extensions.DependencyInjection` for now (full content in Task 8 — the csproj packs it, so it must exist to build).

- [ ] **Step 2: Create the test project**

`tests/RustPlusApi.Extensions.DependencyInjection.UnitTests/RustPlusApi.Extensions.DependencyInjection.UnitTests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
        <IsPackable>false</IsPackable>
        <IsTestProject>true</IsTestProject>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="Microsoft.NET.Test.Sdk" />
        <PackageReference Include="xunit" />
        <PackageReference Include="xunit.runner.visualstudio">
            <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
            <PrivateAssets>all</PrivateAssets>
        </PackageReference>
        <PackageReference Include="coverlet.collector">
            <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
            <PrivateAssets>all</PrivateAssets>
        </PackageReference>
        <PackageReference Include="Microsoft.Extensions.Configuration" />
        <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
    </ItemGroup>

    <ItemGroup>
        <ProjectReference Include="..\..\src\RustPlusApi.Extensions.DependencyInjection\RustPlusApi.Extensions.DependencyInjection.csproj" />
    </ItemGroup>

</Project>
```

Copy the canonical runsettings: `cp tests/RustPlusApi.UnitTests/coverlet.runsettings tests/RustPlusApi.Extensions.DependencyInjection.UnitTests/coverlet.runsettings`

- [ ] **Step 3: Add both projects to the solution**

Run: `dotnet sln add src/RustPlusApi.Extensions.DependencyInjection/RustPlusApi.Extensions.DependencyInjection.csproj tests/RustPlusApi.Extensions.DependencyInjection.UnitTests/RustPlusApi.Extensions.DependencyInjection.UnitTests.csproj`
Expected: both added; `dotnet build` succeeds (empty package project).

- [ ] **Step 4: Write the failing tests**

`tests/RustPlusApi.Extensions.DependencyInjection.UnitTests/RecordingLoggerFactory.cs`:

```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RustPlusApi.Extensions.DependencyInjection.UnitTests;

/// <summary>Records the category names requested from the factory; the loggers themselves are no-ops.</summary>
internal sealed class RecordingLoggerFactory : ILoggerFactory
{
    public List<string> Categories { get; } = [];

    public ILogger CreateLogger(string categoryName)
    {
        Categories.Add(categoryName);
        return NullLogger.Instance;
    }

    public void AddProvider(ILoggerProvider provider)
    {
        // No-op: this factory only records requested categories.
    }

    public void Dispose()
    {
        // Nothing to release.
    }
}
```

`tests/RustPlusApi.Extensions.DependencyInjection.UnitTests/RustPlusServiceCollectionExtensionsTests.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RustPlusApi.Interfaces;
using Xunit;

namespace RustPlusApi.Extensions.DependencyInjection.UnitTests;

public class RustPlusServiceCollectionExtensionsTests
{
    private static RustPlusConnection AnyConnection() => new("127.0.0.1", 28082, 76561198000000000UL, 123456789);

    [Fact]
    public void AddRustPlus_WithConnection_RegistersIRustPlusAsSingleton()
    {
        var services = new ServiceCollection();

        services.AddRustPlus(AnyConnection());

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IRustPlus));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void AddRustPlus_CalledTwice_KeepsASingleRegistration()
    {
        var services = new ServiceCollection();

        services.AddRustPlus(AnyConnection());
        services.AddRustPlus(AnyConnection());

        Assert.Single(services, d => d.ServiceType == typeof(IRustPlus));
    }

    [Fact]
    public async Task AddRustPlus_ResolvesTheSameUnconnectedSingleton()
    {
        var services = new ServiceCollection();
        services.AddRustPlus(AnyConnection());
        await using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IRustPlus>();
        var second = provider.GetRequiredService<IRustPlus>();

        Assert.Same(first, second);
        Assert.False(first.IsConnected);
    }

    [Fact]
    public async Task AddRustPlus_UsesTheContainerLoggerFactory()
    {
        var recorder = new RecordingLoggerFactory();
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(recorder);
        services.AddRustPlus(AnyConnection());
        await using var provider = services.BuildServiceProvider();

        _ = provider.GetRequiredService<IRustPlus>();

        Assert.Contains("RustPlusApi.RustPlusSocket", recorder.Categories);
    }

    [Fact]
    public async Task AddRustPlus_WorksWithoutLoggingRegistered()
    {
        var services = new ServiceCollection();
        services.AddRustPlus(AnyConnection());
        await using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IRustPlus>());
    }

    [Fact]
    public async Task AddRustPlus_AppliesConfigureOptions()
    {
        var services = new ServiceCollection();
        services.AddRustPlus(AnyConnection(), o => o.RequestTimeout = TimeSpan.FromSeconds(5));
        await using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<RustPlusSocketOptions>>();

        Assert.Equal(TimeSpan.FromSeconds(5), options.Value.RequestTimeout);
    }

    [Fact]
    public async Task AddRustPlus_WithConfiguration_BindsTheConnection()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Rust:Server"] = "1.2.3.4",
                ["Rust:Port"] = "28082",
                ["Rust:PlayerId"] = "76561198000000000",
                ["Rust:PlayerToken"] = "123456789",
                ["Rust:UseFacepunchProxy"] = "true",
            })
            .Build();
        var section = config.GetSection("Rust");

        // The binder materialises the positional record (documents the binding contract)…
        Assert.Equal(
            new RustPlusConnection("1.2.3.4", 28082, 76561198000000000UL, 123456789, true),
            section.Get<RustPlusConnection>());

        // …and the registration resolves a client from it.
        var services = new ServiceCollection();
        services.AddRustPlus(section);
        await using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<IRustPlus>());
    }

    [Fact]
    public async Task AddRustPlus_WithEmptyConfigurationSection_ThrowsOnResolve()
    {
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddRustPlus(config.GetSection("Missing"));
        await using var provider = services.BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IRustPlus>());
    }

    [Fact]
    public async Task AddRustPlus_WithConnectionFactory_ResolvesItFromTheProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new RustPlusConnection("9.9.9.9", 1, 1UL, 1));
        services.AddRustPlus(sp => sp.GetRequiredService<RustPlusConnection>());
        await using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IRustPlus>());
    }

    [Fact]
    public async Task ProviderDisposal_DisposesTheSingletonClient()
    {
        var services = new ServiceCollection();
        services.AddRustPlus(AnyConnection());
        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IRustPlus>();

        await provider.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ConnectAsync());
    }

    [Fact]
    public void AddRustPlus_NullArguments_Throw()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddRustPlus(AnyConnection()));
        Assert.Throws<ArgumentNullException>(() => services.AddRustPlus((RustPlusConnection)null!));
        Assert.Throws<ArgumentNullException>(() => services.AddRustPlus((IConfiguration)null!));
        Assert.Throws<ArgumentNullException>(() => services.AddRustPlus((Func<IServiceProvider, RustPlusConnection>)null!));
    }
}
```

`tests/RustPlusApi.Extensions.DependencyInjection.UnitTests/RustPlusFactoryTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace RustPlusApi.Extensions.DependencyInjection.UnitTests;

public class RustPlusFactoryTests
{
    private static RustPlusConnection AnyConnection() => new("127.0.0.1", 28082, 1UL, 1);

    [Fact]
    public void AddRustPlusFactory_RegistersASingletonFactory()
    {
        var services = new ServiceCollection();

        services.AddRustPlusFactory();

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IRustPlusFactory));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void AddRustPlusFactory_NullServices_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddRustPlusFactory());
    }

    [Fact]
    public async Task Create_ReturnsDistinctCallerOwnedClients()
    {
        var services = new ServiceCollection();
        services.AddRustPlusFactory();
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IRustPlusFactory>();

        var first = factory.Create(AnyConnection());
        var second = factory.Create(AnyConnection());

        Assert.NotSame(first, second);

        // Caller-owned: disposing one leaves the other usable.
        ((IDisposable)first).Dispose();
        Assert.False(second.IsConnected);
        ((IDisposable)second).Dispose();
    }

    [Fact]
    public async Task Create_WiresTheContainerLoggerFactory()
    {
        var recorder = new RecordingLoggerFactory();
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(recorder);
        services.AddRustPlusFactory();
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IRustPlusFactory>();

        using var client = (IDisposable)factory.Create(AnyConnection());

        Assert.Contains("RustPlusApi.RustPlusSocket", recorder.Categories);
    }

    [Fact]
    public async Task Create_AppliesConfiguredOptions_AndWorksWithoutLogging()
    {
        var services = new ServiceCollection();
        services.AddRustPlusFactory(o => o.RequestTimeout = TimeSpan.FromSeconds(3));
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IRustPlusFactory>();

        using var client = (IDisposable)factory.Create(AnyConnection());

        Assert.NotNull(client);
    }

    [Fact]
    public async Task Create_WithNullConnection_Throws()
    {
        var services = new ServiceCollection();
        services.AddRustPlusFactory();
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IRustPlusFactory>();

        Assert.Throws<ArgumentNullException>(() => factory.Create(null!));
    }
}
```

Note: the test namespace `RustPlusApi.Extensions.DependencyInjection.UnitTests` resolves `RustPlusConnection`, `IRustPlusFactory` etc. through namespace-hierarchy lookup — do not add `using RustPlusApi;` / `using RustPlusApi.Extensions.DependencyInjection;` (analyzers flag redundant usings).

- [ ] **Step 5: Run to verify failure**

Run: `dotnet test tests/RustPlusApi.Extensions.DependencyInjection.UnitTests/RustPlusApi.Extensions.DependencyInjection.UnitTests.csproj`
Expected: FAIL — `IRustPlusFactory` / `AddRustPlus` do not exist.

- [ ] **Step 6: Implement the package**

`src/RustPlusApi.Extensions.DependencyInjection/IRustPlusFactory.cs`:

```csharp
using RustPlusApi.Interfaces;

namespace RustPlusApi.Extensions.DependencyInjection;

/// <summary>
/// Creates <see cref="IRustPlus"/> clients on demand for connections known only at runtime.
/// Returned clients are owned by the caller, who must dispose them (prefer <c>await using</c>).
/// </summary>
public interface IRustPlusFactory
{
    /// <summary>Creates a new, unconnected client for <paramref name="connection"/>.</summary>
    /// <param name="connection">The server endpoint and player credentials the client connects as.</param>
    /// <returns>A caller-owned <see cref="IRustPlus"/>; call <c>ConnectAsync</c> to connect.</returns>
    IRustPlus Create(RustPlusConnection connection);
}
```

`src/RustPlusApi.Extensions.DependencyInjection/RustPlusFactory.cs`:

```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RustPlusApi.Interfaces;

namespace RustPlusApi.Extensions.DependencyInjection;

/// <summary>
/// Default <see cref="IRustPlusFactory"/>: stamps each created client with the host's logging and
/// the configured socket tuning.
/// </summary>
/// <param name="loggerFactory">The host's logger factory; <see langword="null"/> disables client logging.</param>
/// <param name="options">The configured socket tuning options.</param>
internal sealed class RustPlusFactory(ILoggerFactory? loggerFactory, IOptions<RustPlusSocketOptions> options) : IRustPlusFactory
{
    /// <inheritdoc />
    public IRustPlus Create(RustPlusConnection connection)
    {
        if (connection is null)
        {
            throw new ArgumentNullException(nameof(connection));
        }

        return new RustPlus(connection, options.Value, loggerFactory);
    }
}
```

`src/RustPlusApi.Extensions.DependencyInjection/RustPlusServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RustPlusApi;
using RustPlusApi.Extensions.DependencyInjection;
using RustPlusApi.Interfaces;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers <see cref="IRustPlus"/> clients and the <see cref="IRustPlusFactory"/> into a service collection.</summary>
public static class RustPlusServiceCollectionExtensions
{
    /// <summary>
    /// Registers a singleton <see cref="IRustPlusFactory"/> creating caller-owned <see cref="IRustPlus"/>
    /// clients for connections known only at runtime. The host's <see cref="ILoggerFactory"/> (when
    /// registered) and the configured <see cref="RustPlusSocketOptions"/> are wired into every client.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configureOptions">Optional tuning applied to <see cref="RustPlusSocketOptions"/>.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddRustPlusFactory(
        this IServiceCollection services,
        Action<RustPlusSocketOptions>? configureOptions = null)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        services.AddOptions();
        if (configureOptions is not null)
        {
            services.Configure(configureOptions);
        }

        services.TryAddSingleton<IRustPlusFactory>(static sp => new RustPlusFactory(
            sp.GetService<ILoggerFactory>(),
            sp.GetRequiredService<IOptions<RustPlusSocketOptions>>()));
        return services;
    }

    /// <summary>
    /// Registers a single configured <see cref="IRustPlus"/> client as a container-disposed singleton.
    /// The client is not connected; call <c>ConnectAsync</c> when ready.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="connection">The server endpoint and player credentials the client connects as.</param>
    /// <param name="configureOptions">Optional tuning applied to <see cref="RustPlusSocketOptions"/>.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddRustPlus(
        this IServiceCollection services,
        RustPlusConnection connection,
        Action<RustPlusSocketOptions>? configureOptions = null)
    {
        if (connection is null)
        {
            throw new ArgumentNullException(nameof(connection));
        }

        return services.AddRustPlus(_ => connection, configureOptions);
    }

    /// <summary>
    /// Registers a single configured <see cref="IRustPlus"/> client as a container-disposed singleton,
    /// binding its <see cref="RustPlusConnection"/> from <paramref name="connectionSection"/>
    /// (keys: <c>Server</c>, <c>Port</c>, <c>PlayerId</c>, <c>PlayerToken</c>, optional <c>UseFacepunchProxy</c>).
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="connectionSection">The configuration section the connection is bound from.</param>
    /// <param name="configureOptions">Optional tuning applied to <see cref="RustPlusSocketOptions"/>.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddRustPlus(
        this IServiceCollection services,
        IConfiguration connectionSection,
        Action<RustPlusSocketOptions>? configureOptions = null)
    {
        if (connectionSection is null)
        {
            throw new ArgumentNullException(nameof(connectionSection));
        }

        return services.AddRustPlus(
            _ => connectionSection.Get<RustPlusConnection>()
                 ?? throw new InvalidOperationException(
                     "The configuration section could not be bound to a RustPlusConnection — is the section empty?"),
            configureOptions);
    }

    /// <summary>
    /// Registers a single configured <see cref="IRustPlus"/> client as a container-disposed singleton,
    /// resolving its <see cref="RustPlusConnection"/> from the provider when first requested.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="connectionFactory">Produces the connection from the built provider.</param>
    /// <param name="configureOptions">Optional tuning applied to <see cref="RustPlusSocketOptions"/>.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddRustPlus(
        this IServiceCollection services,
        Func<IServiceProvider, RustPlusConnection> connectionFactory,
        Action<RustPlusSocketOptions>? configureOptions = null)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (connectionFactory is null)
        {
            throw new ArgumentNullException(nameof(connectionFactory));
        }

        services.AddOptions();
        if (configureOptions is not null)
        {
            services.Configure(configureOptions);
        }

        services.TryAddSingleton<IRustPlus>(sp => new RustPlus(
            connectionFactory(sp),
            sp.GetRequiredService<IOptions<RustPlusSocketOptions>>().Value,
            sp.GetService<ILoggerFactory>()));
        return services;
    }
}
```

- [ ] **Step 7: Run the tests — all pass**

Run: `dotnet test tests/RustPlusApi.Extensions.DependencyInjection.UnitTests/RustPlusApi.Extensions.DependencyInjection.UnitTests.csproj`
Expected: PASS (16 tests × 2 TFMs). Then `dotnet build` for the full solution — 0 warnings/errors.

- [ ] **Step 8: Commit**

```bash
git add src/RustPlusApi.Extensions.DependencyInjection tests/RustPlusApi.Extensions.DependencyInjection.UnitTests RustPlusApi.sln
git commit -m "feat: RustPlusApi.Extensions.DependencyInjection package with factory and AddRustPlus"
```

---

## Task 6: FCM DI package + tests

**Files:**

- Create: `src/RustPlusApi.Fcm.Extensions.DependencyInjection/` (csproj + 3 source files)
- Create: `tests/RustPlusApi.Fcm.Extensions.DependencyInjection.UnitTests/` (csproj + 2 source files)
- Modify: `RustPlusApi.sln`

- [ ] **Step 1: Create the package + test projects**

`src/RustPlusApi.Fcm.Extensions.DependencyInjection/RustPlusApi.Fcm.Extensions.DependencyInjection.csproj` — identical shape to Task 5 Step 1 with these substitutions: `PackageId` → `RustPlusApi.Fcm.Extensions.DependencyInjection`, `Product` → `Dependency-injection extensions for RustPlusApi.Fcm.`, `Description` → `Microsoft.Extensions.DependencyInjection registration extensions for the RustPlusApi.Fcm listener.`, `ProjectReference` → `..\RustPlusApi.Fcm\RustPlusApi.Fcm.csproj`, and **drop** the `Microsoft.Extensions.Configuration.Binder` package reference (no config-binding overload for credentials). Placeholder `README.md` with `# RustPlusApi.Fcm.Extensions.DependencyInjection`.

`tests/RustPlusApi.Fcm.Extensions.DependencyInjection.UnitTests/RustPlusApi.Fcm.Extensions.DependencyInjection.UnitTests.csproj` — identical shape to Task 5 Step 2 with the `ProjectReference` pointing at `..\..\src\RustPlusApi.Fcm.Extensions.DependencyInjection\RustPlusApi.Fcm.Extensions.DependencyInjection.csproj` and **without** the `Microsoft.Extensions.Configuration` package reference.

Copy runsettings: `cp tests/RustPlusApi.UnitTests/coverlet.runsettings tests/RustPlusApi.Fcm.Extensions.DependencyInjection.UnitTests/coverlet.runsettings`

Add to solution: `dotnet sln add src/RustPlusApi.Fcm.Extensions.DependencyInjection/RustPlusApi.Fcm.Extensions.DependencyInjection.csproj tests/RustPlusApi.Fcm.Extensions.DependencyInjection.UnitTests/RustPlusApi.Fcm.Extensions.DependencyInjection.UnitTests.csproj`

- [ ] **Step 2: Write the failing tests**

`tests/RustPlusApi.Fcm.Extensions.DependencyInjection.UnitTests/RecordingLoggerFactory.cs`:

```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RustPlusApi.Fcm.Extensions.DependencyInjection.UnitTests;

/// <summary>Records the category names requested from the factory; the loggers themselves are no-ops.</summary>
internal sealed class RecordingLoggerFactory : ILoggerFactory
{
    public List<string> Categories { get; } = [];

    public ILogger CreateLogger(string categoryName)
    {
        Categories.Add(categoryName);
        return NullLogger.Instance;
    }

    public void AddProvider(ILoggerProvider provider)
    {
        // No-op: this factory only records requested categories.
    }

    public void Dispose()
    {
        // Nothing to release.
    }
}
```

`tests/RustPlusApi.Fcm.Extensions.DependencyInjection.UnitTests/RustPlusFcmServiceCollectionExtensionsTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Interfaces;
using Xunit;

namespace RustPlusApi.Fcm.Extensions.DependencyInjection.UnitTests;

public class RustPlusFcmServiceCollectionExtensionsTests
{
    private static Credentials AnyCredentials() => new() { Gcm = new Gcm { AndroidId = 1, SecurityToken = 1 } };

    [Fact]
    public void AddRustPlusFcm_WithCredentials_RegistersIRustPlusFcmAsSingleton()
    {
        var services = new ServiceCollection();

        services.AddRustPlusFcm(AnyCredentials());

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IRustPlusFcm));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public async Task AddRustPlusFcm_ResolvesTheSameSingleton()
    {
        var services = new ServiceCollection();
        services.AddRustPlusFcm(AnyCredentials());
        await using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IRustPlusFcm>();
        var second = provider.GetRequiredService<IRustPlusFcm>();

        Assert.Same(first, second);
    }

    [Fact]
    public async Task AddRustPlusFcm_UsesTheContainerLoggerFactory()
    {
        var recorder = new RecordingLoggerFactory();
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(recorder);
        services.AddRustPlusFcm(AnyCredentials());
        await using var provider = services.BuildServiceProvider();

        _ = provider.GetRequiredService<IRustPlusFcm>();

        Assert.Contains("RustPlusApi.Fcm.RustPlusFcmSocket", recorder.Categories);
    }

    [Fact]
    public async Task AddRustPlusFcm_WithCredentialsFactory_ResolvesThemFromTheProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton(AnyCredentials());
        services.AddRustPlusFcm(sp => sp.GetRequiredService<Credentials>());
        await using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IRustPlusFcm>());
    }

    [Fact]
    public async Task AddRustPlusFcm_AppliesConfigureOptions()
    {
        var services = new ServiceCollection();
        services.AddRustPlusFcm(AnyCredentials(), o => o.HeartbeatInterval = TimeSpan.FromMinutes(1));
        await using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<RustPlusFcmSocketOptions>>();

        Assert.Equal(TimeSpan.FromMinutes(1), options.Value.HeartbeatInterval);
    }

    [Fact]
    public void AddRustPlusFcm_NullArguments_Throw()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddRustPlusFcm(AnyCredentials()));
        Assert.Throws<ArgumentNullException>(() => services.AddRustPlusFcm((Credentials)null!));
        Assert.Throws<ArgumentNullException>(() => services.AddRustPlusFcm((Func<IServiceProvider, Credentials>)null!));
    }

    [Fact]
    public void AddRustPlusFcmFactory_RegistersASingletonFactory()
    {
        var services = new ServiceCollection();

        services.AddRustPlusFcmFactory();

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IRustPlusFcmFactory));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public async Task FactoryCreate_ReturnsDistinctClients_AndWiresLogging()
    {
        var recorder = new RecordingLoggerFactory();
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(recorder);
        services.AddRustPlusFcmFactory();
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IRustPlusFcmFactory>();

        using var first = (IDisposable)factory.Create(AnyCredentials());
        using var second = (IDisposable)factory.Create(AnyCredentials());

        Assert.NotSame(first, second);
        Assert.Contains("RustPlusApi.Fcm.RustPlusFcmSocket", recorder.Categories);
    }

    [Fact]
    public async Task FactoryCreate_WithNullCredentials_Throws()
    {
        var services = new ServiceCollection();
        services.AddRustPlusFcmFactory();
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IRustPlusFcmFactory>();

        Assert.Throws<ArgumentNullException>(() => factory.Create(null!));
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/RustPlusApi.Fcm.Extensions.DependencyInjection.UnitTests/RustPlusApi.Fcm.Extensions.DependencyInjection.UnitTests.csproj`
Expected: FAIL — types/extensions do not exist.

- [ ] **Step 4: Implement the package**

`src/RustPlusApi.Fcm.Extensions.DependencyInjection/IRustPlusFcmFactory.cs`:

```csharp
using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Interfaces;

namespace RustPlusApi.Fcm.Extensions.DependencyInjection;

/// <summary>
/// Creates <see cref="IRustPlusFcm"/> listeners on demand for credentials acquired at runtime
/// (e.g. from <c>FcmRegistration</c>). Returned listeners are owned by the caller, who must
/// dispose them (prefer <c>await using</c>). FCM listeners are single-connection: create a new
/// one to reconnect.
/// </summary>
public interface IRustPlusFcmFactory
{
    /// <summary>Creates a new, unconnected listener for <paramref name="credentials"/>.</summary>
    /// <param name="credentials">The FCM credentials to authenticate with.</param>
    /// <param name="persistentIds">Already-processed message IDs to skip; a fresh list when <see langword="null"/>.</param>
    /// <returns>A caller-owned <see cref="IRustPlusFcm"/>; call <c>ConnectAsync</c> to connect.</returns>
    IRustPlusFcm Create(Credentials credentials, ICollection<string>? persistentIds = null);
}
```

`src/RustPlusApi.Fcm.Extensions.DependencyInjection/RustPlusFcmFactory.cs`:

```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Interfaces;

namespace RustPlusApi.Fcm.Extensions.DependencyInjection;

/// <summary>
/// Default <see cref="IRustPlusFcmFactory"/>: stamps each created listener with the host's logging
/// and the configured tuning.
/// </summary>
/// <param name="loggerFactory">The host's logger factory; <see langword="null"/> disables listener logging.</param>
/// <param name="options">The configured tuning options.</param>
internal sealed class RustPlusFcmFactory(ILoggerFactory? loggerFactory, IOptions<RustPlusFcmSocketOptions> options) : IRustPlusFcmFactory
{
    /// <inheritdoc />
    public IRustPlusFcm Create(Credentials credentials, ICollection<string>? persistentIds = null)
    {
        if (credentials is null)
        {
            throw new ArgumentNullException(nameof(credentials));
        }

        return new RustPlusFcm(credentials, persistentIds ?? [], options.Value, loggerFactory);
    }
}
```

`src/RustPlusApi.Fcm.Extensions.DependencyInjection/RustPlusFcmServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RustPlusApi.Fcm;
using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Extensions.DependencyInjection;
using RustPlusApi.Fcm.Interfaces;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers <see cref="IRustPlusFcm"/> listeners and the <see cref="IRustPlusFcmFactory"/> into a service collection.</summary>
public static class RustPlusFcmServiceCollectionExtensions
{
    /// <summary>
    /// Registers a singleton <see cref="IRustPlusFcmFactory"/> creating caller-owned
    /// <see cref="IRustPlusFcm"/> listeners for credentials acquired at runtime. The host's
    /// <see cref="ILoggerFactory"/> (when registered) and the configured
    /// <see cref="RustPlusFcmSocketOptions"/> are wired into every listener.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configureOptions">Optional tuning applied to <see cref="RustPlusFcmSocketOptions"/>.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddRustPlusFcmFactory(
        this IServiceCollection services,
        Action<RustPlusFcmSocketOptions>? configureOptions = null)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        services.AddOptions();
        if (configureOptions is not null)
        {
            services.Configure(configureOptions);
        }

        services.TryAddSingleton<IRustPlusFcmFactory>(static sp => new RustPlusFcmFactory(
            sp.GetService<ILoggerFactory>(),
            sp.GetRequiredService<IOptions<RustPlusFcmSocketOptions>>()));
        return services;
    }

    /// <summary>
    /// Registers a single configured <see cref="IRustPlusFcm"/> listener as a container-disposed
    /// singleton with its own fresh persistent-ID list. The listener is not connected; call
    /// <c>ConnectAsync</c> when ready.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="credentials">The FCM credentials to authenticate with.</param>
    /// <param name="configureOptions">Optional tuning applied to <see cref="RustPlusFcmSocketOptions"/>.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddRustPlusFcm(
        this IServiceCollection services,
        Credentials credentials,
        Action<RustPlusFcmSocketOptions>? configureOptions = null)
    {
        if (credentials is null)
        {
            throw new ArgumentNullException(nameof(credentials));
        }

        return services.AddRustPlusFcm(_ => credentials, configureOptions);
    }

    /// <summary>
    /// Registers a single configured <see cref="IRustPlusFcm"/> listener as a container-disposed
    /// singleton, resolving its <see cref="Credentials"/> from the provider when first requested.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="credentialsFactory">Produces the credentials from the built provider.</param>
    /// <param name="configureOptions">Optional tuning applied to <see cref="RustPlusFcmSocketOptions"/>.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddRustPlusFcm(
        this IServiceCollection services,
        Func<IServiceProvider, Credentials> credentialsFactory,
        Action<RustPlusFcmSocketOptions>? configureOptions = null)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (credentialsFactory is null)
        {
            throw new ArgumentNullException(nameof(credentialsFactory));
        }

        services.AddOptions();
        if (configureOptions is not null)
        {
            services.Configure(configureOptions);
        }

        services.TryAddSingleton<IRustPlusFcm>(sp => new RustPlusFcm(
            credentialsFactory(sp),
            [],
            sp.GetRequiredService<IOptions<RustPlusFcmSocketOptions>>().Value,
            sp.GetService<ILoggerFactory>()));
        return services;
    }
}
```

- [ ] **Step 5: Run the tests — all pass**

Run: `dotnet test tests/RustPlusApi.Fcm.Extensions.DependencyInjection.UnitTests/RustPlusApi.Fcm.Extensions.DependencyInjection.UnitTests.csproj && dotnet build`
Expected: PASS (10 tests × 2 TFMs); full solution builds clean.

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusApi.Fcm.Extensions.DependencyInjection tests/RustPlusApi.Fcm.Extensions.DependencyInjection.UnitTests RustPlusApi.sln
git commit -m "feat: RustPlusApi.Fcm.Extensions.DependencyInjection package with factory and AddRustPlusFcm"
```

---

## Task 7: Quality gates — Stryker configs, mutation workflow, coverage, testing.md

**Files:**

- Create: `tests/RustPlusApi.Extensions.DependencyInjection.UnitTests/stryker-config.json`
- Create: `tests/RustPlusApi.Fcm.Extensions.DependencyInjection.UnitTests/stryker-config.json`
- Modify: `.github/workflows/Mutation.yml`
- Modify: `docs/development/testing.md`

- [ ] **Step 1: Stryker configs**

`tests/RustPlusApi.Extensions.DependencyInjection.UnitTests/stryker-config.json`:

```json
{
  "stryker-config": {
    "solution": "../../RustPlusApi.sln",
    "test-projects": ["RustPlusApi.Extensions.DependencyInjection.UnitTests.csproj"],
    "target-framework": "net10.0",
    "reporters": ["html", "progress", "cleartext"],
    "thresholds": { "high": 90, "low": 80, "break": 75 },
    "mutate": ["!**/obj/**", "!**/bin/**"],
    "ignore-methods": ["ConfigureAwait", "Log*", "CreateLogger", "SuppressFinalize"],
    "ignore-mutations": [],
    "project-info": { "module": "RustPlusApi.Extensions.DependencyInjection" }
  }
}
```

The FCM one is identical except `"test-projects": ["RustPlusApi.Fcm.Extensions.DependencyInjection.UnitTests.csproj"]` and `"module": "RustPlusApi.Fcm.Extensions.DependencyInjection"`.

- [ ] **Step 2: Extend the mutation workflow matrix**

In `.github/workflows/Mutation.yml`, add to the `include:` list:

```yaml
          - source: RustPlusApi.Extensions.DependencyInjection.csproj
            testdir: tests/RustPlusApi.Extensions.DependencyInjection.UnitTests
          - source: RustPlusApi.Fcm.Extensions.DependencyInjection.csproj
            testdir: tests/RustPlusApi.Fcm.Extensions.DependencyInjection.UnitTests
```

- [ ] **Step 3: (Optional, slow) Run Stryker locally on one DI project**

Run: `cd tests/RustPlusApi.Extensions.DependencyInjection.UnitTests && dotnet tool restore && dotnet stryker --config-file stryker-config.json --project RustPlusApi.Extensions.DependencyInjection.csproj --reporter cleartext ; cd ../..`
Expected: completes with score ≥ 75 (break threshold). Skip if local tooling is unavailable — CI runs weekly.

- [ ] **Step 4: Run the coverage gate**

Run: `tools/coverage/report.sh`
Expected: the merged report includes the two new assemblies; line/branch rates meet the gate. If a branch in the new extensions is uncovered, the report names it — extend the tests (the null-guard and TryAdd branches are all covered by the tests in Tasks 5–6).

- [ ] **Step 5: Update testing.md**

In `docs/development/testing.md`:

1. In the per-project Stryker run-commands list (after the Camera entry), add:

```markdown
# DI extension packages
cd tests/RustPlusApi.Extensions.DependencyInjection.UnitTests
dotnet stryker --config-file stryker-config.json --project RustPlusApi.Extensions.DependencyInjection.csproj

cd tests/RustPlusApi.Fcm.Extensions.DependencyInjection.UnitTests
dotnet stryker --config-file stryker-config.json --project RustPlusApi.Fcm.Extensions.DependencyInjection.csproj
```

2. In the "Achieved mutation scores (net10.0)" table, add:

```markdown
| `RustPlusApi.Extensions.DependencyInjection` | pending | First measured by the next weekly Mutation.yml run. |
| `RustPlusApi.Fcm.Extensions.DependencyInjection` | pending | First measured by the next weekly Mutation.yml run. |
```

3. Add a sentence wherever the SetPlayer-era behaviour is described if present (search `SetPlayer` in the file; remove/adjust any mention).

- [ ] **Step 6: Commit**

```bash
git add tests/RustPlusApi.Extensions.DependencyInjection.UnitTests/stryker-config.json tests/RustPlusApi.Fcm.Extensions.DependencyInjection.UnitTests/stryker-config.json .github/workflows/Mutation.yml docs/development/testing.md
git commit -m "test: wire DI packages into mutation and coverage gates"
```

---

## Task 8: Documentation — DI article, TOC, package READMEs, root README

**Files:**

- Create: `docs/articles/dependency-injection.md`
- Modify: `docs/articles/toc.yml`
- Modify: `src/RustPlusApi.Extensions.DependencyInjection/README.md` (replace placeholder)
- Modify: `src/RustPlusApi.Fcm.Extensions.DependencyInjection/README.md` (replace placeholder)
- Modify: `README.md` (root package table)

- [ ] **Step 1: Write the DocFX article**

`docs/articles/dependency-injection.md`:

````markdown
# Dependency Injection

The `RustPlusApi.Extensions.DependencyInjection` and `RustPlusApi.Fcm.Extensions.DependencyInjection`
packages register the clients into a `Microsoft.Extensions.DependencyInjection` container. Logging
auto-wires from the host's `ILoggerFactory`; tuning flows through `IOptions<RustPlusSocketOptions>` /
`IOptions<RustPlusFcmSocketOptions>`. Nothing auto-connects — you call `ConnectAsync` when ready.

## One configured client

```csharp
// explicit connection
services.AddRustPlus(new RustPlusConnection("12.34.56.78", 28082, playerId, playerToken));

// or bound from configuration (keys: Server, Port, PlayerId, PlayerToken, UseFacepunchProxy)
services.AddRustPlus(configuration.GetSection("Rust"));

// or resolved from the provider
services.AddRustPlus(sp => BuildConnection(sp), o => o.RequestTimeout = TimeSpan.FromSeconds(10));
```

`IRustPlus` resolves as a singleton the container disposes (`IAsyncDisposable`) on shutdown.

## Many or runtime connections — the factory

```csharp
services.AddRustPlusFactory();

// later, when the connection is known:
var factory = provider.GetRequiredService<IRustPlusFactory>();
await using var client = factory.Create(connection);
await client.ConnectAsync();
```

Factory-created clients are **caller-owned**: dispose them yourself.

## FCM

```csharp
// single listener (credentials are secrets — no configuration-binding overload)
services.AddRustPlusFcm(credentials);

// or runtime credentials via the factory (e.g. after FcmRegistration.AcquireCredentialsAsync)
services.AddRustPlusFcmFactory();
var fcm = provider.GetRequiredService<IRustPlusFcmFactory>().Create(credentials);
```

FCM listeners are single-connection: to reconnect, create a new one (another reason the factory fits).

## Lifetimes at a glance

| Registration | Lifetime | Disposed by |
| --- | --- | --- |
| `AddRustPlus(...)` / `AddRustPlusFcm(...)` | Singleton | the container |
| `AddRustPlusFactory()` / `AddRustPlusFcmFactory()` | Singleton (factory) | the container |
| `factory.Create(...)` output | caller-defined | **you** (`await using`) |
````

- [ ] **Step 2: Register it in the TOC**

In `docs/articles/toc.yml`, under `Guides` after the Logging entry:

```yaml
    - name: Dependency Injection
      href: dependency-injection.md
```

- [ ] **Step 3: Package READMEs**

Replace `src/RustPlusApi.Extensions.DependencyInjection/README.md` with:

````markdown
# RustPlusApi.Extensions.DependencyInjection

`Microsoft.Extensions.DependencyInjection` registration extensions for
[`RustPlusApi`](https://www.nuget.org/packages/RustPlusApi).

```csharp
// one configured client (singleton, container-disposed)
services.AddRustPlus(new RustPlusConnection("12.34.56.78", 28082, playerId, playerToken));

// or bound from configuration
services.AddRustPlus(configuration.GetSection("Rust"));

// many / runtime connections: caller-owned clients via the factory
services.AddRustPlusFactory();
await using var client = provider.GetRequiredService<IRustPlusFactory>().Create(connection);
```

Logging auto-wires from the host's `ILoggerFactory`; tuning via `IOptions<RustPlusSocketOptions>`.
Nothing auto-connects — call `ConnectAsync` when ready.

## Documentation

- [Dependency Injection guide](https://handys11.github.io/RustPlusApi/articles/dependency-injection.html)
````

Replace `src/RustPlusApi.Fcm.Extensions.DependencyInjection/README.md` with:

````markdown
# RustPlusApi.Fcm.Extensions.DependencyInjection

`Microsoft.Extensions.DependencyInjection` registration extensions for
[`RustPlusApi.Fcm`](https://www.nuget.org/packages/RustPlusApi.Fcm).

```csharp
// one configured listener (singleton, container-disposed)
services.AddRustPlusFcm(credentials);

// runtime credentials (e.g. from FcmRegistration): caller-owned listeners via the factory
services.AddRustPlusFcmFactory();
await using var fcm = provider.GetRequiredService<IRustPlusFcmFactory>().Create(credentials);
```

Logging auto-wires from the host's `ILoggerFactory`; tuning via `IOptions<RustPlusFcmSocketOptions>`.
FCM listeners are single-connection — create a new one to reconnect.

## Documentation

- [Dependency Injection guide](https://handys11.github.io/RustPlusApi/articles/dependency-injection.html)
````

- [ ] **Step 4: Root README package table**

In the root `README.md` package table (after the `RustPlusApi.Camera` row), add:

```markdown
| [`RustPlusApi.Extensions.DependencyInjection`](src/RustPlusApi.Extensions.DependencyInjection/README.md) | [![NuGet](https://img.shields.io/nuget/v/RustPlusApi.Extensions.DependencyInjection.svg)](https://www.nuget.org/packages/RustPlusApi.Extensions.DependencyInjection) | [![Downloads](https://img.shields.io/nuget/dt/RustPlusApi.Extensions.DependencyInjection.svg)](https://www.nuget.org/packages/RustPlusApi.Extensions.DependencyInjection) | DI registration (`AddRustPlus`, `IRustPlusFactory`) for the core client. |
| [`RustPlusApi.Fcm.Extensions.DependencyInjection`](src/RustPlusApi.Fcm.Extensions.DependencyInjection/README.md) | [![NuGet](https://img.shields.io/nuget/v/RustPlusApi.Fcm.Extensions.DependencyInjection.svg)](https://www.nuget.org/packages/RustPlusApi.Fcm.Extensions.DependencyInjection) | [![Downloads](https://img.shields.io/nuget/dt/RustPlusApi.Fcm.Extensions.DependencyInjection.svg)](https://www.nuget.org/packages/RustPlusApi.Fcm.Extensions.DependencyInjection) | DI registration (`AddRustPlusFcm`, `IRustPlusFcmFactory`) for the FCM listener. |
```

- [ ] **Step 5: Rebuild the DocFX site**

Follow `docs/development/building-docs.md` (typically `docfx docs/docfx.json`). Expected: no broken links; the new article appears in the nav. Do not hand-edit `docs/_site/`.

- [ ] **Step 6: Commit**

```bash
git add docs/articles/dependency-injection.md docs/articles/toc.yml src/RustPlusApi.Extensions.DependencyInjection/README.md src/RustPlusApi.Fcm.Extensions.DependencyInjection/README.md README.md docs/_site
git commit -m "docs: dependency-injection guide, package READMEs, root README rows"
```

---

## Task 9: Final verification

**Files:** none (verification only)

- [ ] **Step 1: Clean Release build**

Run: `dotnet build -c Release`
Expected: Build succeeded, 0 warnings, 0 errors (all projects, both TFMs).

- [ ] **Step 2: Full test suite**

Run: `dotnet test`
Expected: all projects pass on net8.0 and net10.0, including the two new DI test assemblies.

- [ ] **Step 3: Coverage gate**

Run: `tools/coverage/report.sh`
Expected: gate passes with the new assemblies included.

- [ ] **Step 4: net48 smoke build**

Run: `dotnet build tests/RustPlusApi.NetFrameworkSmoke/RustPlusApi.NetFrameworkSmoke.csproj`
Expected: builds — the reshaped core constructor is reachable from `net48` via `netstandard2.0`. (The smoke project intentionally does not reference the DI packages.)

- [ ] **Step 5: No stale surface remains**

Run: `grep -rn "SetPlayer\b" src tests --include='*.cs' | grep -v CanSetPlayerNotes ; grep -rn "LoggerFactory { get" src --include='*.cs' ; grep -rn "{ get; init; }" src/RustPlusApi/RustPlusSocketOptions.cs src/RustPlusApi.Fcm/RustPlusFcmSocketOptions.cs`
Expected: no output from all three.

- [ ] **Step 6: Pack sanity check**

Run: `dotnet pack src/RustPlusApi.Extensions.DependencyInjection/RustPlusApi.Extensions.DependencyInjection.csproj src/RustPlusApi.Fcm.Extensions.DependencyInjection/RustPlusApi.Fcm.Extensions.DependencyInjection.csproj -c Release -o /tmp/dipack`
Expected: two `.nupkg` (+ `.snupkg`) files produced, each containing its README.

- [ ] **Step 7: Commit any verification fixes**

```bash
git add -A
git commit -m "chore: final verification fixes for DI support"
```

(Skip if the tree is clean.)

---

## Self-review notes

- **Spec coverage:** logger reshape + settable options (T2/T3), `SetPlayer` removal (T2), docs ripple (T4), core DI package with factory + 3 overloads (T5), FCM DI package with factory + 2 overloads and fresh `persistentIds` (T6), Stryker/coverage/Mutation.yml/testing.md (T7), DI article + TOC + package/root READMEs (T8), packaging mirrors + CPM pins (T1/T5/T6), sequencing respected (reshape before packages). Out-of-scope items (hosted service, keyed services, smoke-project DI) explicitly excluded.
- **Deviations flagged in header:** `init`→`set` on options (Options-pattern requirement); `Configuration.Binder` instead of `Options.ConfigurationExtensions`.
- **Type consistency check:** `IRustPlusFactory.Create(RustPlusConnection)`, `IRustPlusFcmFactory.Create(Credentials, ICollection<string>?)`, logger categories `"RustPlusApi.RustPlusSocket"` / `"RustPlusApi.Fcm.RustPlusFcmSocket"`, and the 4-param constructors are used identically across tasks.
