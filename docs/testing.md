# Testing Guide

This document describes how to run the test suite, how multi-TFM parity works, how to measure
coverage, how to run mutation testing, and why certain members are excluded from the coverage gate.

---

## Running the suite

Run all tests (both target-framework hosts, both `netstandard2.0` and `net10.0` builds):

```bash
dotnet test tests/RustPlusApi.Tests/RustPlusApi.Tests.csproj
```

Run a single test class on both TFMs:

```bash
dotnet test tests/RustPlusApi.Tests/RustPlusApi.Tests.csproj \
  --filter "FullyQualifiedName~ClassName.MethodName"
```

Run only one TFM (useful to debug a netstandard2.0-specific failure):

```bash
dotnet test tests/RustPlusApi.Tests/RustPlusApi.Tests.csproj -f net8.0
dotnet test tests/RustPlusApi.Tests/RustPlusApi.Tests.csproj -f net10.0
```

Run with coverage (requires the runsettings):

```bash
dotnet test tests/RustPlusApi.Tests/RustPlusApi.Tests.csproj \
  --settings tests/RustPlusApi.Tests/coverlet.runsettings \
  --results-directory ./TestResults
```

Use the helper script for a per-class summary of anything below 100%:

```bash
tools/coverage/report.sh
```

Expected output: two `seq=...% branch=...%` lines (one per TFM), followed by a list of classes still
below 100/100. The list should only contain items documented in the **Coverage exclusion list** section
below (plus any open Phase 8 items).

---

## Multi-TFM parity mechanism

The test project targets `net8.0;net10.0`. The production libraries target `netstandard2.0;net10.0`.

When the test runner uses a `net8.0` host, it cannot load the `net10.0` asset of a multi-targeted
library; the .NET SDK resolves the `netstandard2.0` build instead. When the runner uses a `net10.0`
host, it loads the `net10.0` asset. The same xUnit test suite therefore exercises both compiled
outputs without any duplication.

**Headline case — `HtmlColorParser`:** The `FromHtml` method contains a `#if NET10_0_OR_GREATER`
branch (using `Convert.FromHexString`) and a `#else` branch (manual hex parsing for
`netstandard2.0`). The `net10.0` run covers the `#if` path; the `net8.0` run covers the `#else`
path. Both branches are covered *across* the TFM matrix.

---

## Coverage report artifact: `HtmlColorParser` under net10.0

The net10.0 coverage report shows `HtmlColorParser` at approximately 94%/90% rather than 100/100.
This is a coverlet + `#if` PDB artifact: the `net10.0` compiler does not emit the `#else` block, so
coverlet registers a phantom sequence point for it; the class is genuinely fully covered when both
TFM reports are considered together.

**Do not** add `[ExcludeFromCodeCoverage]` to `HtmlColorParser.cs`. That would discard the real
`net8.0` coverage of the `#else` path.

The Phase 9 coverage gate should evaluate coverage as "every reachable line is covered by at least
one TFM" (merged view) rather than requiring 100% in each per-TFM report independently.

---

## Running mutation testing (Stryker.NET)

The Stryker.NET tool is configured in `tests/RustPlusApi.Tests/stryker-config.json`. It is registered
as a local .NET tool (see `.config/dotnet-tools.json`).

Restore the tool once:

```bash
dotnet tool restore
```

Run mutation testing against the core library (net10.0):

```bash
dotnet stryker \
  --config-file tests/RustPlusApi.Tests/stryker-config.json \
  --project RustPlusApi.csproj
```

Run against a specific project or TFM:

```bash
dotnet stryker \
  --config-file tests/RustPlusApi.Tests/stryker-config.json \
  --project RustPlusApi.Fcm.csproj

dotnet stryker \
  --config-file tests/RustPlusApi.Tests/stryker-config.json \
  --project RustPlusApi.csproj \
  --target-framework net8.0
```

Reports are written to `StrykerOutput/` (git-ignored).

Thresholds (configured in `stryker-config.json`): break at 75%, low at 80%, high at 90%.

---

## Coverage exclusion list

The following members are explicitly excluded from the coverage gate with justifications.
Everything else must reach 100% line and branch coverage across the TFM matrix.

### `SteamLoginService` (whole class)

**File:** `src/RustPlusApi.Fcm.Registration/Steps/SteamLoginService.cs`

**Justification:** Drives a live Chrome/Chromium instance over the Chrome DevTools Protocol (CDP).
There is no meaningful offline seam — the entire class is browser-control logic (launch Chrome,
inject a JavaScript shim via `Page.addScriptToEvaluateOnNewDocument`, navigate to the Facepunch
Steam login URL, capture the OAuth callback token). Mocking CDP would mean reimplementing a browser.
The class is validated by the opt-in `SteamInjectionCanaryTests` canary in
`tests/RustPlusApi.Tests/Canary/` (skipped by default; requires a real Chrome installation).

No pure helpers were extractable: the token arrives via an HTTP query-string parameter on the
loopback callback listener, not from parsing a CDP JSON message, so there is no standalone parsing
logic that could be unit-tested in isolation.

### `FcmRegistration.RegisterWithRustPlusAsync`

**File:** `src/RustPlusApi.Fcm.Registration/FcmRegistration.cs`

**Justification:** Post-guard flow drives live Steam login (`SteamLoginService.LoginAsync`) and the
Rust Companion registration endpoint (`RustCompanionClient.RegisterAsync`). Both are upstream-fragile
live-network calls. The guard (throwing when `ExpoPushToken` is missing) is unit-tested in
`RegistrationOrchestrationTests`. The remainder is validated only by the
`RegistrationCanaryTests.AcquireCredentials_AgainstRealEndpoints_ProducesTokens` canary.

### `RustPlusFcmSocket.ConnectAsync`

**File:** `src/RustPlusApi.Fcm/RustPlusFcmSocket.cs`

**Justification:** Opens a live TLS socket to `mtalk.google.com:5228`, authenticates as an Android
device, sends an MCS `LoginRequest`, and starts a background receive loop. The entire MCS
receive/dispatch pipeline it feeds is covered offline via the `RunReceiveLoopOverStream` seam
(internal method visible to the test assembly) — see `RustPlusFcmSocketTests`. The TCP/TLS connect
and handshake sequence itself requires the live Google endpoint.

### `PairingListener.WaitForServerPairingAsync`

**File:** `src/RustPlusApi.Fcm.Registration/PairingListener.cs`

**Justification:** Calls `_fcm.ConnectAsync()` internally, which requires a live FCM connection (see
above). The pairing-notification mapping helper (`ToServerPairing`) is `internal static` and is fully
unit-tested in `PairingListenerTests` independently of the live flow.

### Generated protobuf contract files (via `ExcludeByFile`)

**Configured in:** `tests/RustPlusApi.Tests/coverlet.runsettings`

**Files excluded:**

| Pattern | What it covers |
|---|---|
| `**/obj/**` | `RustPlusContracts.generated.cs` produced by `protobuf-net.BuildTools` from `src/RustPlusApi/Protobuf/RustPlusContracts.proto` |
| `**/ProtoBuf/Mcs.cs` | Code-first MCS proto contracts (`src/RustPlusApi.Fcm/ProtoBuf/Mcs.cs`) |
| `**/Protobuf/CheckinContracts.cs` | Code-first GCM check-in contracts (`src/RustPlusApi.Fcm.Registration/Protobuf/CheckinContracts.cs`) |

**Justification:** These are mechanically-generated or code-first protobuf DTOs. Their wire
serialization behavior is already locked by `ProtobufRoundTripTests`, `McsRoundTripTests`, and
`RegistrationTests`. The auto-generated `ShouldSerialize*` / `Reset*` / unused-field members
accessed only by the protobuf-net runtime are not worth bespoke unit tests.

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
