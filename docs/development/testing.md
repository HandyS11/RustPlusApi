# Testing Guide

This document describes how to run the test suite, how multi-TFM parity works, how to measure
coverage, how to run mutation testing, and why certain members are excluded from the coverage gate.

---

## Test projects

The monolithic test project was split into **seven** focused projects under `tests/`:

| Source project | Unit tests | Integration tests |
| --- | --- | --- |
| `RustPlusApi` (core) | `RustPlusApi.UnitTests` | `RustPlusApi.IntegrationTests` |
| `RustPlusApi.Fcm` | `RustPlusApi.Fcm.UnitTests` | — (none yet) |
| `RustPlusApi.Fcm.Registration` | `RustPlusApi.Fcm.Registration.UnitTests` | — (none yet) |
| `RustPlusApi.Camera` | `RustPlusApi.Camera.UnitTests` | `RustPlusApi.Camera.IntegrationTests` |
| `RustPlusApi.Extensions.DependencyInjection` | `RustPlusApi.Extensions.DependencyInjection.UnitTests` | — (none yet) |
| `RustPlusApi.Fcm.Extensions.DependencyInjection` | `RustPlusApi.Fcm.Extensions.DependencyInjection.UnitTests` | — (none yet) |

`RustPlusApi.MockServer` is the shared in-process test server used by integration tests.
Integration test projects for `RustPlusApi.Fcm` and `RustPlusApi.Fcm.Registration` will be added
when such tests are written.

---

## Running the suite

Run all tests (both target-framework hosts, both `netstandard2.0` and `net10.0` builds):

```bash
dotnet test RustPlusApi.sln
```

Run a single test class on both TFMs:

```bash
dotnet test RustPlusApi.sln \
  --filter "FullyQualifiedName~ClassName.MethodName"
```

Run only one TFM (useful to debug a netstandard2.0-specific failure):

```bash
dotnet test RustPlusApi.sln -f net8.0
dotnet test RustPlusApi.sln -f net10.0
```

Run with coverage (requires the runsettings):

```bash
dotnet test RustPlusApi.sln \
  --settings tests/RustPlusApi.UnitTests/coverlet.runsettings \
  --results-directory ./TestResults
```

Use the helper script for a per-class summary of anything below 100%:

```bash
tools/coverage/report.sh
```

Expected output: two `seq=...% branch=...%` lines (one per TFM), followed by a list of classes still
below 100/100. The list should only contain items documented in the **Coverage exclusion list** section
below.

---

## Multi-TFM parity mechanism

The test projects target `net8.0;net10.0`. The production libraries target `netstandard2.0;net10.0`.

When the test runner uses a `net8.0` host, it cannot load the `net10.0` asset of a multi-targeted
library; the .NET SDK resolves the `netstandard2.0` build instead. When the runner uses a `net10.0`
host, it loads the `net10.0` asset. The same xUnit test suite therefore exercises both compiled
outputs without any duplication.

**Headline case — `HtmlColorParser`:** The `FromHtml` method contains a `#if NET10_0_OR_GREATER`
branch (delegating to the in-box `ColorTranslator.FromHtml`) and a `#else` branch (manual hex
parsing for `netstandard2.0`, where `ColorTranslator` lives in the Windows-only
`System.Drawing.Common`). The `net10.0` run covers the `#if` path; the `net8.0` run covers the
`#else` path. `HtmlColorParserTests` asserts identical ARGB results for both, so the two
implementations are pinned to agree — and the class reaches 100/100 across the TFM matrix.

---

## Coverage gate

`tools/coverage/report.sh` runs the full test suite, merges the per-project coverage reports via
ReportGenerator into `TestResults/merged/Cobertura.xml`, prints per-class gaps, and then calls
`tools/coverage/check_threshold.py <line_min> <branch_min>` as the CI gate. The gate reads the
**ReportGenerator-merged Cobertura line-rate/branch-rate** (not per-TFM opencover numbers). CI
(`.github/workflows/CI.yml`) runs it at **line 95 / branch 90**.

Achieved at the time of writing: **≈ 96.7% line / 91.4% branch** (merged Cobertura aggregate
across all test projects and both TFMs).
The gap to a literal 100% is irreducible and lives mostly in:

- **Compiler-generated async state-machine branches** — the `MoveNext` fault/continuation arcs in
  `RustPlusSocket.ReceiveAsync`/`ConnectAsync` and similar `async` methods. No deterministic test
  can hit these synthetic branches.
- **Live-socket / live-network lines** — e.g. the non-null `_sslStream?.Close()`/`?.Dispose()`
  cleanup paths and the connect error-invoke, which only execute after a real TLS/WebSocket
  connection. The offline pipelines they feed are covered via test seams; the connect itself is
  `[ExcludeFromCodeCoverage]` (see below).

These are not dead code (so they were not removed) and not cleanly excludable per-branch, so the
gate floor sits below the achieved figures rather than at 100%, with headroom so routine changes
don't trip the gate.

---

## Running mutation testing (Stryker.NET)

Each mutation-tested source project has its own `stryker-config.json` located in its corresponding
unit test project directory. Stryker.NET is registered as a local .NET tool (see
`.config/dotnet-tools.json`).

Restore the tool once:

```bash
dotnet tool restore
```

Run mutation testing against a project (from the test-project directory so the relative
`solution` path in the config resolves):

```bash
cd tests/RustPlusApi.Fcm.UnitTests
dotnet stryker --config-file stryker-config.json --project RustPlusApi.Fcm.csproj

cd tests/RustPlusApi.Fcm.Registration.UnitTests
dotnet stryker --config-file stryker-config.json --project RustPlusApi.Fcm.Registration.csproj

cd tests/RustPlusApi.Camera.UnitTests
dotnet stryker --config-file stryker-config.json --project RustPlusApi.Camera.csproj

cd tests/RustPlusApi.Extensions.DependencyInjection.UnitTests
dotnet stryker --config-file stryker-config.json --project RustPlusApi.Extensions.DependencyInjection.csproj

cd tests/RustPlusApi.Fcm.Extensions.DependencyInjection.UnitTests
dotnet stryker --config-file stryker-config.json --project RustPlusApi.Fcm.Extensions.DependencyInjection.csproj
```

To mutate the `netstandard2.0` build (exercising the `#else` sides of `#if` forks), add
`--target-framework net8.0`.

Reports are written to `StrykerOutput/` (git-ignored). Thresholds (in `stryker-config.json`):
break at 75%, low at 80%, high at 90%. The `.github/workflows/Mutation.yml` workflow runs the
matrix weekly and on manual dispatch.

Logging calls are excluded from mutation via `ignore-methods` (`Log*`, `CreateLogger`): the
`[LoggerMessage]`-generated methods are non-functional diagnostics, so mutating their arguments
would only produce equivalent or low-value mutants.

### Known limitation: the core `RustPlusApi.csproj` cannot be mutated

`RustPlusApi.csproj` crashes Stryker 4.x (`CompilationException` in the rollback compiler) because
`protobuf-net.BuildTools` generates the `RustPlusContracts` types at compile time and Stryker's
instrumented re-compilation cannot resolve them — even with `--mutate '!**/Protobuf/**'`. The core
mappers, `RustPlus`, and `RustPlusSocket` are instead covered by exact-assertion unit tests
(every mapped field, exact error/branch behavior), so their behavior is pinned even though a
mutation score cannot be measured.

### Achieved mutation scores (net10.0)

| Project | Score | Notes |
| --- | --- | --- |
| `RustPlusApi.Camera` | ~90.5% | `CameraController` is exercised by `RustPlusApi.Camera.IntegrationTests` (a `RustPlus` client is required), so that project is listed alongside the unit tests in `test-projects` — Stryker mutates `RustPlusApi.Camera.csproj` and runs both suites against it. Remaining survivors are equivalent: renderer `>>` signed-vs-unsigned shifts; controller keep-alive/`Move` timing (`<` vs `<=` deadline, `duration ?? default`), `|=`→`^=` on a zero-initialised flag accumulator, and movement-gate bitmasks that would only differ on a device advertising one of `Movement`/`SprintAndDuck` but not the other (no such device exists in game). |
| `RustPlusApi.Fcm.Registration` | ~97.1% | Remaining: `CredentialsStore` file-write path + an `AndroidFcmRegister` error-parse equality; the rest is `[ExcludeFromCodeCoverage]` Steam surface. |
| `RustPlusApi.Fcm` | ~83.3% | Remaining: live-socket teardown (`Dispose`/`Close`/`Cancel` statement removals are not observable without leak detection) and equivalent shift/xor mutants in `McsUtils`; `Log*`/`CreateLogger` calls are suppressed via `ignore-methods`. |
| `RustPlusApi` (core) | n/a | Cannot run — see limitation above. |
| `RustPlusApi.Extensions.DependencyInjection` | ~90.9% | Remaining survivors are equivalent: the explicit `if (services is null) throw` guards are masked by the `services.AddOptions()` call, which throws the same `ArgumentNullException(paramName: "services")`. |
| `RustPlusApi.Fcm.Extensions.DependencyInjection` | ~80.0% | Remaining: the same `AddOptions`-masked null-services guards, plus the factory's `persistentIds ?? []` default, which is unobservable without a live FCM connection. |

---

## Coverage exclusion list

The following members are explicitly excluded from the coverage gate with justifications.
Everything else must reach 100% line and branch coverage across the TFM matrix.

### `SteamLoginService.TryOpenBrowser`

**File:** `src/RustPlusApi.Fcm.Registration/Steps/SteamLoginService.cs`

**Justification:** Launches a real OS browser process (`Process.Start`, platform-specific
`xdg-open`/`open`/shell-execute), and failure is by design unobservable from the caller — the
login URL has already been reported through `LoginAsync`'s `onLoginUrl` callback before this runs,
so a headless host can always open the link by hand. The rest of the class — URL construction
(`BuildLoginUrl`), callback parsing (`ParseCallback`), and the loopback `HttpListener` accept loop
in `LoginAsync` — is a plain redirect flow with no browser control involved, and is covered by
`SteamLoginServiceTests` (the `openBrowser: false` seam drives the accept loop with a real
loopback `HttpListener` and `HttpClient`, without opening a browser).

### `FcmRegistration.RegisterWithRustPlusAsync`

**File:** `src/RustPlusApi.Fcm.Registration/FcmRegistration.cs`

**Justification:** Post-guard flow drives live Steam login (`SteamLoginService.LoginAsync`) and the
Rust Companion registration endpoint (`RustCompanionClient.RegisterAsync`). Both are upstream-fragile
live-network calls. The guard (throwing when `ExpoPushToken` is missing) is unit-tested in
`FcmRegistrationTests`. The remainder can only be validated by a real run against the live
endpoints, e.g. via the `RustPlus.Register.ConsoleApp` sample.

### `RustPlusFcmSocket.ConnectAsync`

**File:** `src/RustPlusApi.Fcm/RustPlusFcmSocket.cs`

**Justification:** Opens a live TLS socket to `mtalk.google.com:5228`, authenticates as an Android
device, sends an MCS `LoginRequest`, and starts a background receive loop. The entire MCS
receive/dispatch pipeline it feeds is covered offline via the `RunReceiveLoopOverStream` seam
(internal method visible to the test assembly) — see `FcmSocketFramingTests`/`FcmSocketLifecycleTests`. The TCP/TLS connect
and handshake sequence itself requires the live Google endpoint.

### `PairingListener.WaitForServerPairingAsync`

**File:** `src/RustPlusApi.Fcm.Registration/PairingListener.cs`

**Justification:** Calls `_fcm.ConnectAsync()` internally, which requires a live FCM connection (see
above). The pairing-notification mapping helper (`ToServerPairing`) is `internal static` and is fully
unit-tested in `RegistrationTests` independently of the live flow.

### Generated protobuf contract files (via `ExcludeByFile`)

**Configured in:** `tests/RustPlusApi.UnitTests/coverlet.runsettings` (identical copies exist in
each test project; the CI/tooling scripts use the `RustPlusApi.UnitTests` copy as the canonical
path)

**Files excluded:**

| Pattern | What it covers |
| --- | --- |
| `**/obj/**` | `RustPlusContracts.generated.cs` produced by `protobuf-net.BuildTools` from `src/RustPlusApi/Protobuf/RustPlusContracts.proto` |
| `**/ProtoBuf/Mcs.cs` | Code-first MCS proto contracts (`src/RustPlusApi.Fcm/ProtoBuf/Mcs.cs`) |
| `**/Protobuf/CheckinContracts.cs` | Code-first GCM check-in contracts (`src/RustPlusApi.Fcm.Registration/Protobuf/CheckinContracts.cs`) |

**Justification:** These are mechanically-generated or code-first protobuf DTOs. Their wire
serialization behavior is already locked by `ProtobufRoundTripTests`, `McsRoundTripTests`, and
`RegistrationTests`. The auto-generated `ShouldSerialize*` / `Reset*` / unused-field members
accessed only by the protobuf-net runtime are not worth bespoke unit tests.

### `[LoggerMessage]` generated log methods and `RustPlusConnection` record members

**Files:** `src/RustPlusApi/RustPlusSocketLog.cs`, `src/RustPlusApi.Fcm/RustPlusFcmSocketLog.cs`,
`src/RustPlusApi/RustPlusConnection.cs`

**Justification:** The `[LoggerMessage]` source generator emits the partial log-method bodies with
`[GeneratedCode]`, and the C# compiler emits the record's `Equals`/`GetHashCode`/`ToString`/
`Deconstruct`/copy-constructor/equality members with `[CompilerGenerated]`. Both are already dropped
by the `ExcludeByAttribute` rule (`GeneratedCodeAttribute`, `CompilerGeneratedAttribute`) in the
`coverlet.runsettings` files, with positional property accessors handled by `SkipAutoProps`. No
bespoke tests are required for these members; logging behaviour is exercised through the call sites
in `RustPlusLoggingTests` / `FcmLoggingTests`.

---

## `RustPlusApi.NetFrameworkSmoke` — compile-only guard

The project at `tests/RustPlusApi.NetFrameworkSmoke/` targets `net48` and references the production
libraries (which it resolves via their `netstandard2.0` assets). It is a **compile-only** smoke test:
it proves that the public API surface is reachable from a .NET Framework 4.8 consumer (the lowest
supported platform via `netstandard2.0`).

This project has no runtime tests and does not participate in coverage collection. It may be skipped
on Linux (the `Microsoft.NETFramework.ReferenceAssemblies` package allows the build to succeed on
non-Windows CI, but the resulting binary cannot run on Linux without Mono).

To build it explicitly:

```bash
dotnet build tests/RustPlusApi.NetFrameworkSmoke/RustPlusApi.NetFrameworkSmoke.csproj
```
