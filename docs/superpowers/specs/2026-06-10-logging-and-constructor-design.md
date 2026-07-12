# Design: Library logging & constructor clarity

**Date:** 2026-06-10
**Status:** Approved (pending implementation plan)
**Scope:** `RustPlusApi`, `RustPlusApi.Fcm` (the two socket libraries). `RustPlusApi.Fcm.Registration` and `RustPlusApi.Camera` are explicitly out of scope.

## Problem

1. **No runtime observability.** The libraries' only diagnostics are `Debug.WriteLine(...)` calls, which are `[Conditional("DEBUG")]` and therefore compiled out of the Release NuGet packages. Consumers get zero visibility into connect/receive/teardown/error flows at runtime.
2. **Unclear constructor signatures.** `RustPlus` uses a primary constructor with six positional parameters (`string server, int port, ulong playerId, int playerToken, bool useFacepunchProxy = false, RustPlusSocketOptions? options = null`). Call sites such as `new RustPlus("1.2.3.4", 28082, 76561198…, 12345, true, opts)` are hard to read — the bare `true` and the adjacent numeric IDs are ambiguous.

The two are related: an options object is a natural home for an injected logger, and grouping connection parameters into a record cleans up the signature at the same time.

## Decisions (locked)

- **Logging approach:** `Microsoft.Extensions.Logging.Abstractions` (`ILogger`), defaulting to `NullLogger`. Idiomatic, DI-friendly, tiny dependency, supports `netstandard2.0`.
- **Logger injection:** `ILoggerFactory` supplied via the options object; the library creates categorized loggers internally.
- **Constructor shape:** group connection identity into a `RustPlusConnection` record.
- **Breaking change strategy:** clean break (v2-style). The old positional constructor is removed, not obsoleted.
- **Goal:** consumer observability (apps consuming the package plug in their own logging stack).

## Part 1 — Logging

### Dependency

- Add to `Directory.Packages.props`:
  ```xml
  <PackageVersion Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.8" />
  ```
  (matches the existing 10.0.x line used by `Microsoft.Bcl.AsyncInterfaces`, `System.Text.Json`).
- Reference it (no version attribute — Central Package Management) in both `src/RustPlusApi/RustPlusApi.csproj` and `src/RustPlusApi.Fcm/RustPlusApi.Fcm.csproj`. No TFM condition needed: the abstractions package targets `netstandard2.0`.

### Options surface

Add to both `RustPlusSocketOptions` and `RustPlusFcmSocketOptions`:

```csharp
/// <summary>Factory used to create the library's loggers. When null, logging is disabled
/// (a no-op NullLogger is used). Supply one to route diagnostics into your logging stack.</summary>
public ILoggerFactory? LoggerFactory { get; init; }
```

Keep the properties init-only, consistent with the existing options design.

### Logger resolution

In `RustPlusSocket` and `RustPlusFcmSocket`, resolve a single logger field from the options:

```csharp
private readonly ILogger _logger =
    (options?.LoggerFactory ?? NullLoggerFactory.Instance).CreateLogger(/* see category note */);
```

- Use `GetType()` for the category so concrete subclasses (`RustPlus`, `RustPlusFcm`) log under their real type name. Note: with C# primary constructors, `GetType()` is valid in a field initializer (the instance exists). If analyzer/ordering issues arise, fall back to resolving the logger in a small constructor body or to `CreateLogger<RustPlusSocket>()` / `CreateLogger<RustPlusFcmSocket>()`.
- `NullLoggerFactory.Instance` keeps the field non-null, so call sites never null-check.

### Log events

Replace every `Debug.WriteLine(...)` with a `[LoggerMessage]` source-generated, structured, leveled log method. The source generator ships in the abstractions package and works on both target frameworks. Define the messages as `partial` methods on each socket class (or a dedicated `static partial` log-extensions class per library).

Level mapping (derived from the current `Debug.WriteLine` sites and lifecycle):

| Level | Events |
|-------|--------|
| **Trace** | "Receiving data…", "Waiting for data…", "Received message", per-message receive/dispatch, "Responding to ping" |
| **Information** | connecting, connected, disconnecting, disconnected, receive loop exited |
| **Warning** | unknown broadcast, unknown channel, unknown pairing type, unknown entity type, unrecognized MCS tag, missing AppData, non-Rust notification (missing channelId/body), broadcast-reply matcher threw, expected teardown/reconnect faults |
| **Error** | connect failure (`ConnectAsync` catch), `WebSocketException` drop (receive & send loops), receive-loop generic fault, FCM inactivity timeout |

Use structured named parameters (e.g. `Seq`, `EntityType`, `ChannelId`, `Tag`) rather than interpolated strings, so consumers get structured logs.

### Relationship to events

The existing events (`Connecting`, `Connected`, `ErrorOccurred`, `Disconnected`, …) are unchanged. They serve programmatic handling; logging serves diagnostics. An error path raises `ErrorOccurred` **and** logs at `Error` — both, not either.

## Part 2 — Constructor clarity

### New record (RustPlusApi only)

```csharp
namespace RustPlusApi;

/// <summary>
/// Connection identity for a <see cref="RustPlus"/> client.
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

### Constructor changes

- `RustPlusSocket` primary constructor becomes `(RustPlusConnection connection, RustPlusSocketOptions? options = null)`.
- `RustPlus` primary constructor becomes `(RustPlusConnection connection, RustPlusSocketOptions? options = null) : RustPlusSocket(connection, options)`.
- Internally, `RustPlusSocket` reads `connection.Server`, `connection.Port`, `connection.UseFacepunchProxy` where it builds the URI, and seeds `PlayerId` / `_playerToken` from `connection.PlayerId` / `connection.PlayerToken`. Behaviour is identical to today; only the parameter grouping changes.
- The old six-parameter constructor is **removed** (clean break).

### Documentation migration (do not lose the old XML doc)

The current `RustPlus` and `RustPlusSocket` constructors carry full XML documentation — a `<summary>` plus a `<param>` for each of `server`, `port`, `playerId`, `playerToken`, `useFacepunchProxy`, `options`. This documentation **must be migrated, not dropped**:

- Move the per-parameter `<param>` descriptions onto the corresponding `RustPlusConnection` record parameters (see record above), preserving the exact wording (e.g. "The port dedicated for the Rust+ companion app (not the one used to connect in-game).").
- Update the new constructors' XML doc to describe `connection` and `options`, keeping the class-level `<summary>` and the `<seealso cref="RustPlusSocket"/>` relationship.
- Verify no `<param>` referencing a now-removed parameter remains (would produce a documentation/analyzer warning).

### FCM constructor

`RustPlusFcm` / `RustPlusFcmSocket` constructors are unchanged in shape: `(Credentials credentials, ICollection<string>? persistentIds = null, RustPlusFcmSocketOptions? options = null)`. `Credentials` is already a grouped object, so no record is introduced. FCM gains logging solely through `RustPlusFcmSocketOptions.LoggerFactory`. Its existing constructor XML doc is preserved as-is.

## Testing

- Update all existing unit and integration tests that construct `RustPlus` to the new `RustPlusConnection` shape.
- Add a test asserting the default path uses `NullLogger` (no `LoggerFactory` supplied) and does not throw.
- Add a test supplying a fake/spy `ILoggerFactory` and asserting representative log entries are emitted (e.g. an error path logs at `Error`, an unknown-broadcast path logs at `Warning`). Use a simple in-memory test logger.
- Existing teardown/lifecycle tests must continue to pass unchanged in behaviour.

## Code coverage

The repository enforces **100% line and branch coverage** across the TFM matrix (ReportGenerator-merged
Cobertura via `tools/coverage/check_threshold.py`), with a documented exclusion list in
`docs/development/testing.md`. The runsettings (`tests/**/coverlet.runsettings`) already exclude
`GeneratedCodeAttribute` and `CompilerGeneratedAttribute` via `ExcludeByAttribute`, plus `SkipAutoProps=true`.
Consequences for this change:

- **`[LoggerMessage]` generated method bodies are auto-excluded.** The source generator emits the partial
  log methods with `[GeneratedCode(...)]`, which the existing `ExcludeByAttribute` rule already drops. No
  new `ExcludeByFile`/`ExcludeByAttribute` entry is required for them.
- **`RustPlusConnection` record synthesized members are auto-excluded.** The compiler emits the record's
  `Equals`/`GetHashCode`/`ToString`/`PrintMembers`/`Deconstruct`/copy-constructor/`Clone`/equality-operator
  members with `[CompilerGenerated]`, covered by the existing rule; positional property accessors are handled
  by `SkipAutoProps`. No bespoke record-equality test is needed for the gate (add one only if desired for
  behavioural confidence).
- **New branches that DO count — must be covered:**
  - The logger-resolution fallback `options?.LoggerFactory ?? NullLoggerFactory.Instance` is a branch. Cover
    **both** sides: a construction with no `LoggerFactory` (null → NullLogger) and one with a supplied factory.
    This applies to both `RustPlusSocket` and `RustPlusFcmSocket`.
  - Every new log **call site** placed on a conditional path (e.g. the warning logged in an `unknown
    channel`/`unknown entity type` branch) sits on a line that the coverage gate counts. These paths already
    have tests (`FcmSocketLifecycleTests`, `FcmSocketFramingTests`, the core dispatch/mapper tests); adding a
    log call on an already-covered line does not change coverage, but any *new* guard introduced for logging
    must be exercised.
- **Update `docs/development/testing.md`** to note (under the coverage-exclusion section) that `[LoggerMessage]`
  generated log methods and record-synthesized members are covered by the existing `GeneratedCodeAttribute`
  / `CompilerGeneratedAttribute` exclusions — so reviewers understand why they are absent from the gate.
- No change to the `coverlet.runsettings` files is anticipated; if a specific generated artifact is found to
  leak into the report, prefer extending `ExcludeByAttribute`/`ExcludeByFile` consistently across **all five**
  runsettings copies (they are kept identical).

## Mutation testing (Stryker)

Mutation testing runs per project (`.github/workflows/Mutation.yml`, weekly + manual). Among the in-scope
libraries, **only `RustPlusApi.Fcm.csproj` is mutated** — core `RustPlusApi.csproj` is intentionally omitted
(protobuf-net.BuildTools generated types defeat Stryker's rollback compiler). So the Stryker work is confined
to FCM:

- **`tests/RustPlusApi.Fcm.UnitTests/stryker-config.json`** — the `ignore-methods` array currently lists
  `["ConfigureAwait", "Debug.WriteLine", "SuppressFinalize"]`. Since `Debug.WriteLine` is being removed in
  favour of `ILogger`, replace its entry with a logging ignore so Stryker does not generate (equivalent,
  non-functional) mutants on logging calls and their message/argument literals:
  - If the `[LoggerMessage]` partial methods are named with a `Log` prefix, add a single wildcard entry
    `"Log*"` (Stryker `ignore-methods` supports `*` wildcards), which also covers any direct
    `LogWarning`/`LogError`/… extension calls. Keep `ConfigureAwait` and `SuppressFinalize`.
  - Also add `"CreateLogger"` so the logger-resolution call is not mutated.
- The `[LoggerMessage]`-generated method bodies live under `obj/` and are already excluded by the config's
  `mutate` globs (`!**/obj/**`), so the generator output itself is not mutated.
- For consistency, apply the same `ignore-methods` edit to the Camera and Fcm.Registration stryker configs
  even though those libraries are out of scope for logging in this change (keeps the three configs aligned;
  harmless where no logging exists yet).
- After the change, the FCM mutation score must hold at or above the existing thresholds
  (`high: 90, low: 80, break: 75`). The new constructor/logging code in FCM (the `LoggerFactory` resolution
  branch) is exercised by the coverage tests above, so it should not introduce surviving mutants beyond the
  ignored logging calls.
- **Update `docs/development/testing.md`** (the Stryker thresholds / configuration section, ~lines 113–140)
  to reflect the `ignore-methods` change (logging replaces `Debug.WriteLine`).

## Documentation (READMEs + DocFX site)

### READMEs

- Update `src/RustPlusApi/README.md` and `src/RustPlusApi.Fcm/README.md`:
  - Show the new `new RustPlus(new RustPlusConnection(...), options)` call site.
  - Add a short "Logging" snippet showing how to pass an `ILoggerFactory` via options.

### DocFX articles (`docs/articles/`)

The DocFX site (Rust-themed, rebuilt in PR #60) must be updated so examples compile against the new API:

- **Constructor call sites** — update every `new RustPlus(...)` (the **RustPlus client**, not `RustPlusFcm`)
  example to the `RustPlusConnection` form. Files containing client constructor examples to review:
  `getting-started.md`, `rustplus-client.md`, `recipes.md`, `samples.md`, `troubleshooting.md`, `index.md`.
  `new RustPlusFcm(...)` examples (e.g. in `fcm-notifications.md`, `credentials.md`) are **unchanged in shape**
  — only revisit them if adding a logging snippet. Grep both `new RustPlus(` and `new RustPlusFcm(` and
  classify each hit before editing.
- **New "Logging" documentation** — add a dedicated section or short article showing how to route library
  diagnostics into a consumer's logging stack via `RustPlusSocketOptions.LoggerFactory` /
  `RustPlusFcmSocketOptions.LoggerFactory` (e.g. wiring an `ILoggerFactory` from
  `Microsoft.Extensions.Logging`). If a new article file is added, register it in the DocFX TOC
  (`toc.yml`) so it appears in the site navigation.
- **`docs/development/testing.md`** — apply the coverage-exclusion note and the Stryker `ignore-methods`
  update described in the sections above.
- Rebuild/verify the site per `docs/development/building-docs.md` so the generated `docs/_site/` reflects the
  changes (do not hand-edit `_site/`).

## Out of scope

- `RustPlusApi.Fcm.Registration` — constructor already clean (all-optional); short-lived request flow, low logging value. Optional follow-up.
- `RustPlusApi.Camera` — pure renderer, no socket/connection. Optional follow-up.
- Obsolete-shim migration path — explicitly rejected in favour of a clean break.

## Open questions

None outstanding.
