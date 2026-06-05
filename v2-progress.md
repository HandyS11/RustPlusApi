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
| 1 | Feature-parity prep + drop legacy (clan, nexus) | ☑ |
| 2 | Single Protobuf dependency (protobuf-net) | ☑ |
| 3 | Code-first protos | ☑ |
| 4 | Multi-target `netstandard2.0;net10.0` | ☑ |
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
- [◐] Broadcast injection — `BroadcastAsync` + team-chat, smart-switch, and clan (chat/changed) builders done; camera builders come with Phase 5
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

- [x] Port clan (`GetClanInfoAsync`, `SetClanMotdAsync`, `GetClanChatAsync`, `SendClanMessageAsync`) to typed `RustPlus` + `IRustPlus`
- [x] Add `OnClanChatReceived` / `OnClanChanged` events + models (`Data/Clans/*`, `ClanMessageEventArg`, `ClanChangedEventArg`) + mappers (`AppClanInfoToModel`, `AppClanChatToModel`)
- [x] Add `GetNexusAuthAsync` → `Response<NexusAuth?>` (`Data/NexusAuth.cs`, `AppNexusAuthToModel`)
- [x] Map each legacy method to a modern equivalent (lift before drop) — clan/nexus lifted; all other legacy methods already had typed equivalents
- [x] Delete `RustPlusLegacy.cs`
- [x] Clean all `*Legacy*` call sites; `grep -ri legacy src samples` is clean. Only `SendRequestAsync` returns raw `AppMessage` — the intentional low-level send primitive (custom-request escape hatch), not a legacy wrapper
- [x] Strip legacy sections from both READMEs (root + `src/RustPlusApi`); added clan events to the examples
- [x] **Bonus:** fixed the `IsError` success-inversion bug — `Response.Success` was treated as an error, so `PromoteToLeaderAsync`/`SetSubscriptionAsync`/`SetClanMotdAsync`/`SendClanMessageAsync` never reported success. Covered by a regression test
- _Tests: +5 clan mapper units, +7 clan/nexus integration (incl. broadcast events & the IsError guard) → 26 total green_

---

## Phase 2 — Single Protobuf dependency (§2)

**Done when:** `Google.Protobuf` removed from `Directory.Packages.props`; all round-trip fixtures green; every former `is null` presence check re-verified.

- [x] Convert `RustPlusContracts.proto` → protobuf-net classes via `protogen` (committed `RustPlusContracts.cs`: 20.3k → 1.3k lines)
- [x] **Proto field names → snake_case** (camelCase→snake, field numbers preserved → wire-identical) so `protogen` emits uniform PascalCase matching the existing C# API; matches the repo's `mcs.proto` convention. _(decision: §14)_
- [x] Swap serializer calls in `RustPlusSocket.cs` (`Serializer.Deserialize`/`Serializer.Serialize` over `MemoryStream`)
- [x] Re-validate every presence check — message fields stay null-when-unset (`IsError`, `ParseNotification`, `Success is not null`, `ClanInfo` null); optional scalars moved from Google `HasX` → protobuf-net `ShouldSerializeX()` (clan mapper); `.Types.` nesting → direct; `ByteString`→`byte[]`
- [x] Remove `Google.Protobuf`; `protobuf-net` on core csproj + `Directory.Packages.props`
- [◐] Regression-test wire format — 4 protobuf round-trip unit tests + the full integration suite (client↔mock, both protobuf-net) green. **Real-server capture still pending** (§15.4 golden payloads) — field numbers are preserved so the wire is unchanged, but a captured-payload check is the belt-and-suspenders guard
- _Tests: 30 green (added `ProtobufRoundTripTests`)._

---

## Phase 3 — Code-first protos (§3)

**Done when:** `mcs.proto`/`Mcs.cs` gone (hand-written types); `RustPlusContracts.cs` no longer committed (build-gen) or regenerated via protogen; build clean on both TFMs.

- [x] Hand-write MCS `[ProtoContract]` types (`Mcs.cs`, ~230 lines vs 856 generated); deleted `mcs.proto`. Optional scalars → nullable; kept exact public names the FCM socket relies on (incl. `auth_service`/`type` enum-clash cases + pluralized `AppDatas`/`Settings`/`ReceivedPersistentIds`); adjusted 2 nullable consumption sites in `RustPlusFcmSocket`
- [x] Build-time gen for `RustPlusContracts` via `protobuf-net.BuildTools` — its Roslyn source generator compiles the `.proto` (added as `<AdditionalFiles>`) at build; **deleted the committed `RustPlusContracts.cs`** (generated into `obj/`). The preferred option worked, not the fallback
- [x] Verify build clean on both TFMs — confirmed in Phase 4: the build-time proto generation + hand-written MCS both compile under `netstandard2.0` and `net10.0`
- _Tests: 35 green (added `McsRoundTripTests`: heartbeat/login/data-message round-trips + bidirectional tag mapping)._

---

## Phase 4 — Multi-target `netstandard2.0;net10.0` (§1)

**Done when:** `dotnet pack` emits `lib/netstandard2.0` + `lib/net10.0`; a net48 smoke app references the package and constructs `RustPlus`.

- [x] Removed forced global TFM from `Directory.Build.props`; added `LangVersion=latest`; non-library projects (samples/tests/mock) pinned to `net10.0`
- [x] Set `<TargetFrameworks>netstandard2.0;net10.0</TargetFrameworks>` on the two shipping libs only
- [x] Closed ns2.0 BCL gaps: `System.Text.Json` package (FCM, conditional); `IsExternalInit` polyfill for records/`init` (both libs). `ClientWebSocket` was already in the ns2.0 surface — no package needed
- [x] Audited runtime-only APIs and guarded with `#if`: `TcpClient.ConnectAsync(…, ct)` (net10) vs `(host, port)` (ns2.0); `Interlocked.Increment(ref uint)` → `_seq` changed to `int` + cast; `ColorTranslator.FromHtml` → `HtmlColorParser` (in-box on net10, hand-rolled hex parser on ns2.0, avoiding the Windows-only `System.Drawing.Common`)
- [x] net48 smoke app (`test/RustPlusApi.NetFrameworkSmoke`) references both libs (via their ns2.0 asset) and constructs `RustPlus` — compiles on Linux CI via `Microsoft.NETFramework.ReferenceAssemblies`
- _Acceptance: `dotnet pack` emits `lib/netstandard2.0/` + `lib/net10.0/` for both packages; net48 consumer compiles. 35 tests still green._

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
- [x] **(extra) `IsError` success-inversion** — `Response.Success` was wrongly treated as an error; fixed in Phase 1 ([RustPlusSocket.cs](src/RustPlusApi/RustPlusSocket.cs)) with a regression test
