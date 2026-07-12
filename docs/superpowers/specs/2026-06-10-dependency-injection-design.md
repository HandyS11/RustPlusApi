# Design: Dependency-injection support for RustPlusApi

**Date:** 2026-06-10
**Status:** Approved (pending implementation plan)
**Scope:** A breaking, pre-release reshape of how the socket libraries receive their logger, plus two new
DI extension packages that make the clients first-class citizens of `Microsoft.Extensions.DependencyInjection`.

## Problem

The socket clients (`RustPlus`, `RustPlusFcm`) are constructed with runtime data and an options bag, and
expose their logging hook as `ILoggerFactory` sitting on that options bag. This is workable but not
idiomatic for DI consumers:

- An `ILoggerFactory` is a *service*, not a config-bindable value, yet it lives on `RustPlusSocketOptions` /
  `RustPlusFcmSocketOptions` alongside timeouts and buffer sizes. That blocks a clean Options pattern (you
  cannot bind the options class wholesale from `appsettings.json`, and embedding a service in an
  `IOptions<T>` snapshot is an anti-pattern).
- There is no `AddRustPlus(...)` registration surface, no factory for multiple/dynamic connections, and no
  automatic wiring of the host's `ILoggerFactory`. Consumers must hand-write factory delegates.

The logging feature that introduced `options.LoggerFactory` is **not yet released**, so reshaping it now
costs real users nothing; doing it after release would be a genuine break.

## Decisions (locked)

- **Usage model:** both a **factory** primitive (for many/dynamic connections) and a **single configured
  client** convenience (for the one-client case).
- **Logger reshape:** move logging from `options.LoggerFactory` to a constructor-injected `ILoggerFactory?`;
  remove `LoggerFactory` from both options classes. Breaking, done pre-release.
- **Packaging:** two extension packages — `RustPlusApi.Extensions.DependencyInjection` and
  `RustPlusApi.Fcm.Extensions.DependencyInjection` — keeping the core packages dependency-light.
- **Lifecycle:** registration only. The integration does not connect the client; the consumer calls
  `ConnectAsync`/`DisconnectAsync`. No `IHostedService` in v1.
- **Identity is creation-time and immutable:** remove `RustPlusSocket.SetPlayer` (the lone runtime-identity
  mutation). "New identity → new instance" holds everywhere; the factory is the runtime-credentials story.
- **Out of scope (YAGNI for v1):** keyed/named multi-singletons (the factory covers multiplicity), and any
  auto-connect / reconnect / retry behaviour.

## Part 1 — Core library reshape (breaking, pre-release)

### Options classes become pure tuning

Remove the `LoggerFactory` property (and its `using Microsoft.Extensions.Logging;`) from:

- `src/RustPlusApi/RustPlusSocketOptions.cs`
- `src/RustPlusApi.Fcm/RustPlusFcmSocketOptions.cs`

After this, both classes hold only config-bindable values (timeouts, keep-alive, buffer size; heartbeat /
inactivity), so a consumer can `services.Configure<RustPlusSocketOptions>(config.GetSection("Rust:Options"))`.

### Constructors gain an `ILoggerFactory?` parameter

- `RustPlusSocket` / `RustPlus`:
  `(RustPlusConnection connection, RustPlusSocketOptions? options = null, ILoggerFactory? loggerFactory = null)`
- `RustPlusFcmSocket` / `RustPlusFcm`:
  `(Credentials credentials, ICollection<string>? persistentIds = null, RustPlusFcmSocketOptions? options = null, ILoggerFactory? loggerFactory = null)`

The logger field resolution is unchanged except for its source — `options.LoggerFactory` becomes the new
`loggerFactory` parameter:

```csharp
private readonly ILogger _logger =
    (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger("RustPlusApi.RustPlusSocket");
```

`Microsoft.Extensions.Logging.Abstractions` stays referenced by the core libraries (already present).
Behaviour for non-DI consumers is identical: omit the argument → `NullLogger`, zero overhead.

### Remove `SetPlayer` (breaking, pre-release)

Delete `RustPlusSocket.SetPlayer(ulong, int)` (`src/RustPlusApi/RustPlusSocket.cs`). It is the only
runtime-identity mutation in the library, required a non-atomic-swap concurrency caveat in its own doc
comment, and contradicts the "client identity is creation-time and immutable" invariant this design
establishes. A consumer who needs a different player creates a new instance (via the factory in DI contexts).

Consequences:

- `PlayerId` becomes get-only (`protected ulong PlayerId { get; }`) and `_playerToken` becomes `readonly`;
  the `PlayerId` doc comment drops its `<see cref="SetPlayer"/>` reference.
- Remove the `SetPlayer_ChangesCredentialsOnNextRequest` test from
  `tests/RustPlusApi.IntegrationTests/SocketLifecycleTests.cs` and update that file's class-level doc comment
  (it lists SetPlayer among its covered areas).
- `SetPlayer` is not on `IRustPlusSocket`, so no interface change. Generated DocFX API pages regenerate.

### Ripple updates (part of this change)

- `tests/RustPlusApi.UnitTests/RustPlusLoggingTests.cs` and
  `tests/RustPlusApi.Fcm.UnitTests/FcmLoggingTests.cs`: change
  `new RustPlusSocketOptions { LoggerFactory = factory }` constructions to pass the factory via the new
  constructor parameter (e.g. `new RustPlus(connection, options: null, loggerFactory: factory)` and the FCM
  `TestSocket`/`TestableRustPlusFcm` equivalents). The behavioural assertions (NullLogger default, warning
  emission) are unchanged.
- READMEs `src/RustPlusApi/README.md`, `src/RustPlusApi.Fcm/README.md`: update the "Logging" snippets to the
  constructor-injected form.
- DocFX `docs/articles/logging.md`: update its snippets to the constructor-injected form.

## Part 2 — `RustPlusApi.Extensions.DependencyInjection`

New packable project `src/RustPlusApi.Extensions.DependencyInjection/`, TFMs `netstandard2.0; net10.0`,
referencing the `RustPlusApi` project plus `Microsoft.Extensions.DependencyInjection.Abstractions`,
`Microsoft.Extensions.Options`, and `Microsoft.Extensions.Options.ConfigurationExtensions`. Namespace
`RustPlusApi.Extensions.DependencyInjection`; the `Add*` extensions live on
`Microsoft.Extensions.DependencyInjection.IServiceCollection` (conventional namespace
`Microsoft.Extensions.DependencyInjection` for discoverability).

### Factory primitive

```csharp
namespace RustPlusApi.Extensions.DependencyInjection;

/// <summary>Creates <see cref="IRustPlus"/> clients on demand for connections known at runtime.
/// Returned clients are owned by the caller, who must dispose them.</summary>
public interface IRustPlusFactory
{
    IRustPlus Create(RustPlusConnection connection);
}
```

Implementation is an `internal sealed class RustPlusFactory(ILoggerFactory loggerFactory,
IOptions<RustPlusSocketOptions> options) : IRustPlusFactory`, registered as a **singleton** (it is stateless
aside from the injected services). `Create` returns
`new RustPlus(connection, options.Value, loggerFactory)`.

Registration:

```csharp
public static IServiceCollection AddRustPlusFactory(
    this IServiceCollection services,
    Action<RustPlusSocketOptions>? configureOptions = null);
```

- Registers `IRustPlusFactory` as a singleton.
- If `configureOptions` is supplied, calls `services.Configure(configureOptions)`; otherwise the default
  `RustPlusSocketOptions` is used. (Consumers may also configure options independently with the standard
  `services.Configure<RustPlusSocketOptions>(...)`.)

### Single configured client

Registers `IRustPlus` as a **singleton** that the container disposes via `IAsyncDisposable`. Three overloads
differ only in how the connection is supplied:

```csharp
// explicit connection value
public static IServiceCollection AddRustPlus(
    this IServiceCollection services,
    RustPlusConnection connection,
    Action<RustPlusSocketOptions>? configureOptions = null);

// connection (and optionally options) bound from configuration
public static IServiceCollection AddRustPlus(
    this IServiceCollection services,
    IConfiguration connectionSection,
    Action<RustPlusSocketOptions>? configureOptions = null);

// connection resolved from the provider at build time
public static IServiceCollection AddRustPlus(
    this IServiceCollection services,
    Func<IServiceProvider, RustPlusConnection> connectionFactory,
    Action<RustPlusSocketOptions>? configureOptions = null);
```

Each overload's registration delegate resolves `ILoggerFactory` and `IOptions<RustPlusSocketOptions>` from
the provider and constructs `new RustPlus(connection, options.Value, loggerFactory)`. The config-binding
overload uses `connectionSection.Get<RustPlusConnection>()` (positional-record constructor binding, supported
by `Microsoft.Extensions.Configuration.Binder`).

### Lifetime & disposal semantics

- **Single configured `IRustPlus`** → Singleton; the container disposes it (`IAsyncDisposable`) on provider
  disposal. One long-lived client per registration.
- **Factory-created clients** → owned by the caller; the caller disposes (`await using`). The factory does
  not track its output. The factory itself is a Singleton.
- Nothing auto-connects: the consumer calls `ConnectAsync`/`DisconnectAsync`.

## Part 3 — `RustPlusApi.Fcm.Extensions.DependencyInjection`

New packable project `src/RustPlusApi.Fcm.Extensions.DependencyInjection/` mirroring Part 2, referencing the
`RustPlusApi.Fcm` project plus the same DI/Options packages. It accounts for FCM's construction inputs.

### Factory primitive

```csharp
namespace RustPlusApi.Fcm.Extensions.DependencyInjection;

public interface IRustPlusFcmFactory
{
    IRustPlusFcm Create(Credentials credentials, ICollection<string>? persistentIds = null);
}
```

`internal sealed class RustPlusFcmFactory(ILoggerFactory loggerFactory,
IOptions<RustPlusFcmSocketOptions> options) : IRustPlusFcmFactory`; `Create` returns
`new RustPlusFcm(credentials, persistentIds, options.Value, loggerFactory)`. Registered singleton via:

```csharp
public static IServiceCollection AddRustPlusFcmFactory(
    this IServiceCollection services,
    Action<RustPlusFcmSocketOptions>? configureOptions = null);
```

### Single configured client

```csharp
public static IServiceCollection AddRustPlusFcm(
    this IServiceCollection services,
    Credentials credentials,
    Action<RustPlusFcmSocketOptions>? configureOptions = null);

public static IServiceCollection AddRustPlusFcm(
    this IServiceCollection services,
    Func<IServiceProvider, Credentials> credentialsFactory,
    Action<RustPlusFcmSocketOptions>? configureOptions = null);
```

Registers `IRustPlusFcm` as a Singleton (container-disposed). **No config-binding overload** for credentials:
`Credentials` are secrets loaded from `FcmRegistration` or a credential store, not from `appsettings.json`.

### `persistentIds` nuance

`persistentIds` is a mutable collection the client mutates at runtime (it records processed message IDs). To
avoid cross-instance state bleed:

- The single-client registration gives the client its own fresh list (a new `List<string>()` per
  registration) unless the consumer supplies one.
- The factory takes `persistentIds` per `Create` call (caller-controlled), defaulting to a fresh list.

## Testing

New xUnit test projects, each with its own `coverlet.runsettings` (copied from the canonical one) and a
`stryker-config.json` (these packages are pure C# with no generated protobuf, so they are mutation-testable
and must meet the existing thresholds `high:90, low:80, break:75`):

- `tests/RustPlusApi.Extensions.DependencyInjection.UnitTests`
- `tests/RustPlusApi.Fcm.Extensions.DependencyInjection.UnitTests`

Each test project's `.csproj` mirrors the existing test projects (TFMs `net8.0; net10.0`, xUnit, coverlet) and
adds the new package(s) to `Mutation.yml`'s matrix.

Test cases (per package, adapted):

- **Registration shape:** each `Add*` overload registers the expected service type with the expected lifetime
  (`ServiceDescriptor` assertions: `IRustPlus`/`IRustPlusFcm` Singleton; `IRustPlusFactory`/`IRustPlusFcmFactory`
  Singleton).
- **Resolution:** `BuildServiceProvider().GetRequiredService<IRustPlus>()` returns a usable, not-yet-connected
  client (`IsConnected == false`); resolving twice returns the same singleton instance.
- **Options wiring:** a non-default `RustPlusSocketOptions` configured via `configureOptions` or
  `services.Configure<>` is the one the resolved client uses (assert an observable effect, e.g. a tuning value
  surfaced through a test seam, or — minimally — that construction with the configured options does not throw
  and `IsConnected == false`).
- **Logger wiring:** a spy `ILoggerFactory` registered in the container is the one the client logs through —
  drive a warning path (unknown broadcast / unknown channel via the existing testable-subclass seam) and assert
  the spy captured it. This proves the container's `ILoggerFactory` reaches the client.
- **Factory semantics:** `IRustPlusFactory.Create(...)` returns distinct instances on repeated calls; instances
  are caller-owned (disposing one does not affect another); the factory is a singleton.
- **Config binding:** the `IConfiguration` overload binds a `RustPlusConnection` from an in-memory configuration
  section (`Server`/`Port`/`PlayerId`/`PlayerToken`/`UseFacepunchProxy`).

The DI extension code is hand-written (not generated), so it is subject to the repository coverage gate; the
tests above are designed to exercise every overload and branch.

## Documentation

- New DocFX article `docs/articles/dependency-injection.md` (registered in the articles `toc.yml`) covering the
  factory, the three registration styles, options binding, logger auto-wiring, and the lifetime/disposal rules.
- A `README.md` for each new package (packed into the NuGet package), with the canonical `Add*` snippets.
- Update the existing logging docs touched in Part 1 (`docs/articles/logging.md`, both core READMEs) to the
  constructor-injected logger form.
- `docs/development/testing.md`: add the two new mutation-tested projects to the achieved-scores table and note
  their `stryker-config.json` location, consistent with the existing entries.

## Packaging

- Two new packable `.csproj` mirroring the existing library projects (PackageId, multi-target, authors/owners,
  license, `RepositoryUrl`, `PackageReadmeFile`, `PackageIcon`, `IncludeSymbols`/snupkg).
- Add `PackageVersion` entries to `Directory.Packages.props` for `Microsoft.Extensions.DependencyInjection.Abstractions`,
  `Microsoft.Extensions.Options`, and `Microsoft.Extensions.Options.ConfigurationExtensions` (10.0.x line, matching
  the existing pins).
- Add both projects (and their test projects) to the solution.

## Sequencing (for the plan)

1. **Part 1 (core reshape)** lands first — the DI packages depend on the new constructor signature. After it,
   the whole solution must build and the existing suite (including the updated logging tests) must stay green.
2. **Part 2** (`RustPlusApi.Extensions.DependencyInjection`) + its tests/docs.
3. **Part 3** (`RustPlusApi.Fcm.Extensions.DependencyInjection`) + its tests/docs.
4. Cross-cutting docs (DI article, testing.md, READMEs) and packaging wiring.

## Out of scope

- Keyed/named multi-singleton registration (the factory is the multiplicity primitive). Possible follow-up on
  `net8.0+` via keyed services.
- `IHostedService`/auto-connect, reconnect, and retry policies. The consumer owns connection lifecycle.
- DI support for `RustPlusApi.Fcm.Registration` and `RustPlusApi.Camera` (not connection clients).

## Open questions

None outstanding.
