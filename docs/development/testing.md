# Testing Guide

This document describes how to run the test suite, how multi-TFM parity works, how to measure
coverage, how to run mutation testing, and why certain members are excluded from the coverage gate.

---

## Test projects

The monolithic test project was split into **seven** focused projects under `tests/`, plus one
covering the build-time proto tool:

| Source project | Unit tests | Integration tests |
| --- | --- | --- |
| `RustPlusApi` (core) | `RustPlusApi.UnitTests` | `RustPlusApi.IntegrationTests` |
| `RustPlusApi.Fcm` | `RustPlusApi.Fcm.UnitTests` | — (none yet) |
| `RustPlusApi.Fcm.Registration` | `RustPlusApi.Fcm.Registration.UnitTests` | — (none yet) |
| `RustPlusApi.Camera` | `RustPlusApi.Camera.UnitTests` | `RustPlusApi.Camera.IntegrationTests` |
| `RustPlusApi.Extensions.DependencyInjection` | `RustPlusApi.Extensions.DependencyInjection.UnitTests` | — (none yet) |
| `RustPlusApi.Fcm.Extensions.DependencyInjection` | `RustPlusApi.Fcm.Extensions.DependencyInjection.UnitTests` | — (none yet) |

`ProtoGen.UnitTests` covers `tools/update-proto/ProtoGen`, the tool that regenerates
`RustPlusContracts.proto` from the decompiled Rust server. It is the one test project that does
**not** follow the multi-TFM parity rule below: ProtoGen is a `net10.0`-only build-time tool rather
than a shipped library, so the project targets `net10.0` alone. It is likewise excluded from the
coverage gate (`[ProtoGen]*` in `coverlet.runsettings`), matching Sonar's existing `**/tools/**`
coverage exclusion — the tool must not move the shipped libraries' aggregate in either direction.

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

The test projects target `net8.0;net10.0` (except `ProtoGen.UnitTests`, see above). The production
libraries target `netstandard2.0;net10.0`.

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

Achieved at the time of writing: **≈ 97.2–97.5% line / 94.2–94.3% branch** for the libraries and
**99.56% line / 98.54% branch** for the web app (merged Cobertura aggregates across all test
projects and both TFMs).

The library figure is quoted as a range because it genuinely is one: three consecutive runs of the
same commit produced 97.22, 97.33 and 97.45% line. The variance is entirely `RustPlusSocket`
(89.17–90.68% line across those runs), whose teardown and concurrent-dispose arms are covered or not
depending on how the integration tests' real WebSocket teardown happens to interleave. **Do not read
a small movement in this number as a regression or an improvement** — compare per-class figures for
the class you actually changed, and expect `RustPlusSocket` to drift on its own. This run-to-run
noise is part of why the gate floor sits at 95/90 rather than at the achieved figure.
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

## SonarQube coverage

`.github/workflows/Sonar.yml` reports coverage to SonarQube as a **single pre-merged report**, not
as the raw per-project/per-TFM opencover files.

This matters because of the parity mechanism above. `dotnet test` writes one opencover report per
test project *per TFM*, and a file with a `#if NET10_0_OR_GREATER` fork is only fully covered when
those reports are unioned — the `net10.0` report covers the `#if` lines, the `net8.0` one covers the
`#else` lines. SonarQube does not union them: handed the raw set it settles on one report per file,
so every line the other TFM covered is reported as uncovered. `HtmlColorParser` is the clearest
casualty — genuinely 100/100 across the matrix, but previously shown at 56.5% line coverage.

So the workflow runs ReportGenerator first (the same merge `tools/coverage/report.sh` gates on),
emits `-reporttypes:SonarQube` to `TestResults/sonarqube/SonarQube.xml`, and passes it via
`sonar.coverageReportPaths` (the generic coverage format) instead of
`sonar.cs.opencover.reportsPaths`.

The analysis run uses **`tools/coverage/sonar.runsettings`** rather than the per-test-project
`coverlet.runsettings`. The two are identical apart from `UseSourceLink`, which is deliberately
omitted for Sonar: with it on, coverlet writes `raw.githubusercontent.com` URLs in place of file
paths and SonarQube cannot match them against the files it indexed. Keeping the exclusions in sync
is what makes SonarQube measure the same thing the local gate does — if you change one file's
`Exclude`/`ExcludeByFile`/`ExcludeByAttribute` rules, change both.

### Why `sonar.coverage.exclusions` is not just `samples`/`tests`/`tools`

Keeping the runsettings in sync is necessary but not sufficient, because SonarQube does **not** take
its "lines to cover" denominator from the coverage report. Each language analyzer computes the
executable lines itself, and any line the report says nothing about is counted as uncovered. Two
consequences, both of which the exclusion list has to absorb:

| Pattern | Why |
| --- | --- |
| `**/wwwroot/**` | `apps/RustPlusApi.CredentialsWeb/wwwroot/app.js` is ~400 lines of browser JavaScript. Sonar's JS analyzer indexes it and reports 197 executable lines; coverlet, being a .NET tool, cannot produce coverage for it at any TFM. Left in, it read as a flat 0% and single-handedly moved the project from ~97% to 91.7% — a .NET coverage figure dominated by a file no .NET test can reach. **This is a real, acknowledged gap, not a solved one:** `app.js` has no tests. Closing it means feeding `sonar.javascript.lcov.reportPaths` from a JavaScript test runner, which is a deliberate future decision, not an oversight. The app directory does carry a small Node toolchain already, but for linting only: `npm run lint` there runs ESLint with typescript-eslint and eslint-plugin-sonarjs (`eslint.config.mjs`), the local mirror of the JavaScript rules SonarQube applies, so a JS code smell fails in CI and in the pre-push hook instead of surfacing on develop after the merge. |
| `**/apps/**/Program.cs` | SonarC# honours `[ExcludeFromCodeCoverage]` when it computes executable lines, which is why `PairingListener` and `FcmRegistration` read 100% despite their excluded members. It does not connect the attribute to **top-level statements**: `Program.cs`'s statements are not syntactically inside the `partial class Program;` part that carries the attribute, so all 36 lines were counted even though coverlet excludes them correctly (they never appear in the local web gap list). The file-level exclusion restates, for Sonar, the exclusion the attribute already expresses. |

Neither entry weakens the local gate — `tools/coverage/report.sh` never saw either file to begin
with. They exist to stop SonarQube measuring something different from what CI gates on.

---

## Credentials web app

`apps/RustPlusApi.CredentialsWeb` targets `net10.0` only — it is an ASP.NET Core app, not a
multi-targeted library — so it sits outside the multi-TFM parity mechanism described above and is
exercised on a single TFM host rather than two.

It is gated separately, at the same **95% line / 90% branch** bar as the libraries, via a second
ReportGenerator merge that `tools/coverage/report.sh` produces and checks alongside the library one:
`TestResults/merged-web/Cobertura.xml`, filtered to just `RustPlusApi.CredentialsWeb`. The library
merge (`TestResults/merged/Cobertura.xml`) explicitly filters the app assembly *out*, so the
library gate stays exactly what it was before the app existed — folding a net10.0-only app into
that aggregate would change the bar the libraries have to clear.

Two members are excluded from the app's coverage gate:

- **`Program`** (`apps/RustPlusApi.CredentialsWeb/Program.cs`) — host wiring: composition only,
  exercised end to end by the endpoint tests.
- **`LiveRegistrationSteps`** (`apps/RustPlusApi.CredentialsWeb/Upstream/LiveRegistrationSteps.cs`)
  — a live-network seam: every member drives Google, Expo, Facepunch or the MCS socket and cannot
  be validated offline. All logic above it is tested against the `IRegistrationSteps` abstraction.

`SessionSweeper` (`apps/RustPlusApi.CredentialsWeb/Sessions/SessionSweeper.cs`) is **not** excluded,
but its per-class figures in `tools/coverage/report.sh`'s gap list (currently ~75% line / 50%
branch) are expected rather than a regression to chase. `ExecuteAsync`'s `catch (Exception ex)`
around `store.SweepExpired()` is retained defensive coding — the right response if a future change
to what `SessionStore.SweepExpired()`/`Session.Dispose()` can throw ever reintroduces a failure
mode — but nothing reachable today throws there: `Session.Dispose()` was fixed to never throw (see
`Session.Dispose`'s remarks), which was the only source of an exception on that path. The branch is
therefore permanently unreachable through legitimate application behaviour, and inventing a
contrived throw just to exercise it would test nothing real. The app's aggregate gate absorbs this
without difficulty (headroom same as the libraries, above).

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
| `RustPlusApi.Fcm.Registration` | ~89.6% | 389 killed, 23 timeout, 47 survived, 1 no coverage. Remaining survivors are mostly diagnostic/progress strings and statements (`SteamLoginService` status/warning messages and best-effort cleanup calls, `BrowserLocator` and `LoginProfile` logging/cleanup statements, `CdpSession` and `WebSocketCdpTransport` dispose/cleanup statements); boundary mutants on byte-size and retry limits (`RustPlusAppLogin` HTTP status ranges and URL-length checks, `LoginProfile` stale-profile age and retry loop, `BrowserLaunchArguments` whitespace probe, `AndroidFcmRegister` separator index); and the `CredentialsStore` file-write path (the one no-coverage mutant). Live-network members stay excluded (see the coverage exclusion list below). |
| `RustPlusApi.Fcm` | ~83.3% | Remaining: live-socket teardown (`Dispose`/`Close`/`Cancel` statement removals are not observable without leak detection) and equivalent shift/xor mutants in `McsUtils`; `Log*`/`CreateLogger` calls are suppressed via `ignore-methods`. |
| `RustPlusApi` (core) | n/a | Cannot run — see limitation above. |
| `RustPlusApi.Extensions.DependencyInjection` | ~90.9% | Remaining survivors are equivalent: the explicit `if (services is null) throw` guards are masked by the `services.AddOptions()` call, which throws the same `ArgumentNullException(paramName: "services")`. |
| `RustPlusApi.Fcm.Extensions.DependencyInjection` | ~80.0% | Remaining: the same `AddOptions`-masked null-services guards, plus the factory's `persistentIds ?? []` default, which is unobservable without a live FCM connection. |

---

## Coverage exclusion list

The following members are explicitly excluded from the coverage gate with justifications.
Everything else is expected at 100% line and branch coverage across the TFM matrix. Where a spot
is neither reasonably excludable nor reachable by a test, it must be enumerated with a
justification instead of silently left short — see the exclusion entries below for
the pattern.

### `ChromiumLauncher`

**File:** `src/RustPlusApi.Fcm.Registration/Steps/Browser/ChromiumLauncher.cs`

**Justification:** The whole class is excluded. It starts a real browser process with a fresh
profile, waits for the debugging port that browser picks and reports in its `DevToolsActivePort`
file, and talks to it over a real WebSocket. It is validated only by the manual Linux and Windows
runs of `RustPlus.Register.ConsoleApp`. Everything it plugs into is tested over fakes: the browser
search (`BrowserLocator`), the launch arguments (`BrowserLaunchArguments`), the profile lifecycle
(`LoginProfile`), the port file and the endpoint discovery (`DevToolsEndpoint`, over real temporary
files and a stub HTTP handler), the CDP session (`CdpSession`) and the login orchestration
(`RustPlusAppLogin`, `SteamLoginService`). The wait for the browser to exit is the same on both TFMs
(`WaitForExit(int)` on a worker thread, not .NET 10's `WaitForExitAsync`, which also waits for the
redirected output streams to close). Its one `#if` fork is in the kill of a browser that is stuck or
never opened its port: .NET 10 kills the whole process tree, while netstandard2.0 has no
`Kill(bool)` and kills the started process only.

### `SteamLoginService.LoginAsync(Action<string>?, CancellationToken)`

**File:** `src/RustPlusApi.Fcm.Registration/Steps/SteamLoginService.cs`

**Justification:** The public overload is a one-line composition root: it passes
`SystemBrowserEnvironment.Instance` and `new ChromiumLauncher()` to the internal overload, so
calling it starts a real browser. All the orchestration (browser choice, paste fallback, profile
cleanup, error mapping) lives in the internal overload and is covered through its environment and
launcher seams.

### `WebSocketCdpTransport.ConnectAsync`

**File:** `src/RustPlusApi.Fcm.Registration/Steps/Cdp/WebSocketCdpTransport.cs`

**Justification:** Opens a real `ClientWebSocket` to the browser's debugging endpoint. The rest of
the class (send, receive, close) is tested over a loopback TCP socket pair; only the connect needs a
live DevTools endpoint.

### `FcmRegistration.RegisterWithRustPlusAsync`

**File:** `src/RustPlusApi.Fcm.Registration/FcmRegistration.cs`

**Justification:** Past the guard it drives a real browser (`SteamLoginService.LoginAsync`) and the
live Rust Companion endpoint (`RustCompanionClient.RegisterAsync`). Both are upstream-fragile
live calls. The guard (throwing when `ExpoPushToken` is missing) is unit-tested in
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

### `System.Index` / `System.Range` netstandard2.0 polyfill

**File:** `src/RustPlusApi.Fcm.Registration/Polyfills/IndexRange.cs`

**Justification:** Pure compiler scaffolding. `netstandard2.0` has no `System.Index`/`System.Range`,
and the C# compiler refuses `..`/`^` syntax unless those types exist somewhere — so the polyfill has
to be compiled into the `netstandard2.0` asset even though nothing calls it. Roslyn lowers every
current use site (`SteamLoginService.ParseQuery`'s `pair[..separator]` / `pair[(separator + 1)..]`)
straight to `string.Substring`, so not a single member of either struct is reachable at runtime and
no test can make one execute. Excluding it keeps 20 permanently-unreachable lines and 12 branches
out of the library gate's denominator. If a future use site takes an actual `Index`/`Range` value
(e.g. a from-end `^n`), the calls become reachable and this exclusion should be dropped in favour of
real tests.

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

### Accepted residual gaps (not excluded, deliberately not chased)

Per the "no unjustified gaps" rule these are enumerated rather than silently left short. Each one is
reachable only through a contrivance that would assert nothing real — the same standard applied to
`SessionSweeper` above.

- **`CredentialsStore.Save`'s `if (!OperatingSystem.IsWindows())`** (1 branch) — inside a
  `#if NET10_0_OR_GREATER` fork. CI and local development both run Linux, so only the true arm ever
  executes; the Windows arm needs a Windows host, not a better test. Covering it means adding a
  Windows leg to the matrix for one branch.
- **`SessionStore`'s two `Interlocked.CompareExchange` retry loops** (2 lines, 2 branches) — the
  arm taken when the CAS *loses* a race. Reaching it deterministically means pausing one thread
  inside the loop body, which requires a seam that exists only to be tested; the loops' actual
  contract (never exceeding `MaxConcurrentPairings`) is already pinned by `SessionStoreCapsTests`.
- **`RustPlusSocket`'s teardown and concurrent-dispose paths** (~37 lines, ~14 branches) — the
  `catch (ObjectDisposedException)` arms around `_lifecycleLock.Release()`, the `WebSocketException`/
  `OperationCanceledException` arms around the close handshake, and the `completed != loop` arms of
  the `Task.WhenAny` teardown bounds. Every one requires a dispose to land inside a specific window
  of another operation, or a real socket to break mid-close. These are the "irreducible" lines the
  **Coverage gate** section refers to; an audit in 2026-09 confirmed the characterisation and found
  only `Dispose(bool)`'s finalizer arm and the default `ParseNotification` extension point to be
  cleanly reachable — both are now covered by `RustPlusSocketBaseTests`, which adds exactly three
  lines (the `if (!disposing) return;` pair and `ParseNotification`'s body) plus the `!disposing`
  branch. Because these arms depend on a race, **this class's coverage is not reproducible run to
  run** — it moved between 89.17% and 90.68% line across three runs of one commit. That instability
  is itself the evidence for the claim above: a line a test cannot reliably reach is a line no test
  is really covering.

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
