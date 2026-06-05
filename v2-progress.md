# RustPlusApi — v2 Refactor Progress

> Tracking document for the v2 modernization work described in [v2-plan.md](v2-plan.md).
> Branch: `refactor/v2` → target `develop`. Release: single **`2.0.0`** cut.
>
> Status legend: ☐ not started · ◐ in progress · ☑ done · ⊘ blocked

Last updated: 2026-06-05

---

## At-a-glance

| Phase | Theme | Status |
|---|---|---|
| 0 | Unblock & safety net (CI, analyzers, mock server, reliability bugs) | ◐ |
| 1 | Feature-parity prep + drop legacy (clan, nexus) | ☐ |
| 2 | Single Protobuf dependency (protobuf-net) | ☐ |
| 3 | Code-first protos | ☐ |
| 4 | Multi-target `netstandard2.0;net10.0` | ☐ |
| 5 | Camera system (protocol + optional rendering) | ☐ |
| 6 | Native credential acquisition | ☐ |
| 7 | JSON cleanup | ☐ |
| 8 | Docs & release (`2.0.0`) | ☐ |
| ✶ | Proto-refresh tooling (`tools/update-proto/`) | ☐ |
| ✶ | One-time golden-payload capture | ☐ |

---

## Phase 0 — Unblock & safety net (§9, §10, §11)

**Done when:** CI builds on .NET 10; `dotnet test` runs ≥1 real test; mock server accepts a connection and replays one canned `AppMessage`; SonarAnalyzer + curated `WarningsAsErrors` on; coverage imported; Sonar gate required on PRs; reliability bugs (§10 items 1–5, 8) fixed.

### CI/CD & analyzers

- [x] Bump `actions/setup-dotnet` to `10.0.x` in `CI.yml` and `CD.yml` (also added it to `Sonar.yml`, which had no SDK step)
- [x] Add a `dotnet test` step to CI — CI.yml's existing step now runs against the new test project
- [x] Enable `EnableNETAnalyzers` + `AnalysisLevel=latest-Recommended` in `Directory.Build.props`
- [x] Add `SonarAnalyzer.CSharp` package (PrivateAssets=all) — referenced from both libraries
- [◐] Curated `WarningsAsErrors` promoted: `S2930;S2931;S3881;S112;CA2201;CA2213` (dispose pattern + exception hygiene). `CA2007` left out until §10 item-9 (ConfigureAwait) lands; `S2221` left out (would block top-level catch handlers)
- [◐] Add `pull_request` trigger to `Sonar.yml` (done); make the Quality Gate a required check (GitHub branch-protection setting, not in-repo)
- [x] Wire coverage — `Sonar.yml` now runs `dotnet test --collect:"XPlat Code Coverage;Format=opencover"` → `sonar.cs.opencover.reportsPaths` (verified the opencover file is produced locally)

### Mock server & test project (§9)

- [x] `test/RustPlusApi.MockServer` — `HttpListener`/WebSocket server speaking the real wire protocol; shares the contract assembly so request/response stay in lockstep
- [x] Scripted scenarios / canned `AppMessage` responses (`MockResponses`: info/time/map/entity/error builders + custom responder hook)
- [◐] Broadcast injection — `BroadcastAsync` + team-chat & smart-switch builders done; clan/camera builders come with Phases 1/5
- [ ] Optional mock FCM data-message emitter — not done (explicitly optional)
- [x] `test/RustPlusApi.Tests` (xUnit) — MCS varint framing + `Extensions/*` mapper units (14 tests green)
- [x] Integration test: real `RustPlus` client against `ws://localhost` — GetInfo/GetTime, error path, and a team-chat broadcast event

### Reliability hardening (§10 items 1–5, 8)

- [x] 1 — Implement real `Dispose()` (socket, `SslStream`, `TcpClient`, `ClientWebSocket`, CTS) in both sockets — proper `Dispose(bool)` pattern
- [x] 2 — Remove fire-and-forget async in `RustPlusSocket.DisconnectAsync` (awaited the delay + close directly)
- [x] 3 — Replace library `Console.WriteLine` with `Debug.WriteLine` in `RustPlusFcmSocket.OnDataMessage`
- [x] 4 — Stop throwing base `Exception` in `RustPlusFcmSocket.ReceiveMessages` (→ `InvalidOperationException`; dropped the `CA2201` pragma)
- [x] 5 — Don't throw on unknown tag in `RustPlusFcmSocket.OnMessage` (now logs + degrades)
- [x] 8 — Fix `EncodeVarInt32(0)` returning an empty array (`while` → `do/while`)

---

## Phase 1 — Feature parity prep + drop legacy (§4, §5b, §5c)

**Done when:** clan + nexus exposed as `Response<T>`; `RustPlusLegacy.cs` deleted; `grep -ri legacy src samples` empty; no public API returns raw `AppMessage`.

- [ ] Port clan (`getClanInfo`, `setClanMotd`, `getClanChat`, `sendClanMessage`) to typed `RustPlus`
- [ ] Add `OnClanChatReceived` / `OnClanChanged` events + models
- [ ] Add `GetNexusAuthAsync` → `Response<NexusAuth>`
- [ ] Map each legacy method to a modern equivalent (lift before drop)
- [ ] Delete `RustPlusLegacy.cs`
- [ ] Clean all `*Legacy*` call sites; verify no public method returns raw `AppMessage`
- [ ] Strip legacy sections from READMEs

---

## Phase 2 — Single Protobuf dependency (§2)

**Done when:** `Google.Protobuf` removed from `Directory.Packages.props`; all round-trip fixtures green; every former `is null` presence check re-verified.

- [ ] Convert `RustPlusContracts.proto` → protobuf-net classes via `protogen`
- [ ] Swap serializer calls in `RustPlusSocket.cs` (parse/serialize)
- [ ] Re-validate every presence check (`IsError`, `Extensions/*` mappers, `ShouldSerialize*`)
- [ ] Remove `Google.Protobuf`; add `protobuf-net` to core csproj + `Directory.Packages.props`
- [ ] Regression-test wire format against captured fixtures

---

## Phase 3 — Code-first protos (§3)

**Done when:** `mcs.proto`/`Mcs.cs` gone (hand-written types); `RustPlusContracts.cs` no longer committed (build-gen) or regenerated via protogen; build clean on both TFMs.

- [ ] Hand-write MCS `[ProtoContract]` types; delete `mcs.proto` + `Mcs.cs`
- [ ] Build-time gen for `RustPlusContracts` via `protobuf-net.BuildTools` (fallback: commit `protogen` output)
- [ ] Verify build clean on both TFMs

---

## Phase 4 — Multi-target `netstandard2.0;net10.0` (§1)

**Done when:** `dotnet pack` emits `lib/netstandard2.0` + `lib/net10.0`; a net48 smoke app references the package and constructs `RustPlus`.

- [ ] Remove forced global TFM from `Directory.Build.props`; keep neutral settings
- [ ] Set `<TargetFrameworks>netstandard2.0;net10.0</TargetFrameworks>` on shipping libs only
- [ ] Close ns2.0 BCL gaps (`System.Text.Json`, `ClientWebSocket` if flagged)
- [ ] Audit runtime-only APIs; guard with `#if NET10_0_OR_GREATER`
- [ ] net48 smoke app references the package and constructs `RustPlus`

---

## Phase 5 — Camera system (§5a)

**Done when:** subscribe/input/unsubscribe + `OnCameraRaysReceived` work against the mock; `RustPlusApi.Camera` renders a fixture frame to a non-empty image.

### Protocol layer (core, must-have)

- [ ] `SubscribeToCameraAsync` / `SendCameraInputAsync` / `UnsubscribeFromCameraAsync`
- [ ] `OnCameraRaysReceived` event carrying typed `CameraFrame`
- [ ] `controlFlags`/`buttons` modeled as `[Flags]` enum
- [ ] `CameraFrame` / `CameraEntity` models (reuse rustplus-desktop shape)

### Rendering layer (stretch — separate package)

- [ ] `RustPlusApi.Camera` package depending on `SixLabors.ImageSharp`
- [ ] `rayData` RLE decode → image (port from rustplus.js `Camera.js` / olijeffers0n `camera_manager.py`)
- [ ] Develop test-first against a captured `AppCameraRays` fixture

---

## Phase 6 — Native credential acquisition (§7)

**Done when:** `RustPlus.Register.ConsoleApp` takes a user from zero → `rustplus.config.json` → a live `RustPlus` connection, once, by hand.

- [ ] New package `RustPlusApi.Fcm.Registration`
- [ ] Step 1–3: GCM check-in, FIS, FCM register (mirror current `@liamcottle/push-receiver`)
- [ ] Step 4: Expo push token
- [ ] Step 5: Steam OpenID login via local `HttpListener` + browser
- [ ] Step 6: register device with Rust Companion
- [ ] Step 7: persist `Credentials` via `System.Text.Json`
- [ ] Convenience `PairingListener` (event surface modeled on rustplus-desktop `IPairingListener`)
- [ ] `samples/RustPlus.Register.ConsoleApp`
- [ ] Opt-in registration "canary" integration test (separate from default CI gate)
- [ ] Centralize Firebase/Expo constants in one `RustPlusConstants` file

---

## Phase 7 — JSON cleanup (§8)

**Done when:** no bespoke converters remain except justified ones; all (de)serialization via STJ.

- [ ] Inventory all serialization; standardize on `System.Text.Json`
- [ ] Delete converters STJ handles natively (`Int32StringConverter`, `StringToUInt64Converter`); keep only needed
- [ ] Isolate credential model behind the registration package

---

## Phase 8 — Docs & release (§12, §11)

**Done when:** three+ package READMEs packed; DocFX site builds; both packages tagged `2.0.0`; `MIGRATION.md` published.

- [ ] Per-package READMEs (`RustPlusApi`, `RustPlusApi.Fcm`, `.Registration`, `.Camera`)
- [ ] Trim root README (project intro, package matrix + badges, quickstart, DocFX links)
- [ ] DocFX site under `docs/` + `docs.yml` GitHub Pages workflow
- [ ] Replace ".NET 8 or later" → ".NET Standard 2.0+ / .NET 10"
- [ ] Document camera/clan/nexus usage; make native credential flow the primary path
- [ ] `MIGRATION.md` (legacy→modern mapping, serializer change, new TFMs, new packages)
- [ ] Bump both packages `1.4.0` → `2.0.0`; new packages ship at `2.0.0`

---

## Cross-cutting / standalone tasks

### Proto-refresh tooling (§6 — Method A)

**Done when:** `tools/update-proto/` runs end-to-end and reproduces the committed proto from a fresh server download.

- [ ] SteamCMD fetch of dedicated server (app `258550`)
- [ ] Decompile contract types with `ilspycmd`
- [ ] Regenerate `RustPlusContracts.proto` via `Serializer.GetProto<AppMessage>()`; diff & PR
- [ ] Document monthly (first-Thursday) rerun routine; keep Method D runbook as backup

### One-time golden-payload capture (§15.4)

**Done when:** golden fixtures committed under `test/fixtures/`.

- [ ] Pairing FCM body
- [ ] A handful of `AppMessage` responses
- [ ] A short `AppCameraRays` stream (hex/base64)

### Code quality (§10 — cross-cutting, applies to every phase)

- [ ] "Clean as You Code" gate on all PRs (0 new bugs/vulns, coverage-on-new-code ≥ target)
- [ ] Item 6 — replace busy-poll loops (send pump, disconnect wait)
- [ ] Item 7 — correlate responses by `seq`, not FIFO order
- [ ] Item 9 — add `ConfigureAwait(false)` to library awaits
- [ ] Item 10 — rename `Utils` type; remove commented-out code / TODO blocks
