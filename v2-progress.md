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
| 5 | Camera system (protocol + optional rendering) | ☑ |
| 6 | Native credential acquisition | ☑ |
| 7 | JSON cleanup | ☑ |
| 8 | Docs & release (`2.0.0`) | ☑ |
| ✶ | Proto-refresh tooling (`tools/update-proto/`) | ☑ tooling; ◐ apply refresh |
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

- [x] `SubscribeToCameraAsync` / `SendCameraInputAsync` / `UnsubscribeFromCameraAsync` on `RustPlus` + `IRustPlus`
- [x] `OnCameraRaysReceived` event carrying typed `CameraFrame` (wired into `ParseNotification`)
- [x] `CameraButtons` + `CameraControlFlags` `[Flags]` enums — exact wire values fetched from liamcottle/rustplus.js
- [x] `CameraFrame` / `CameraEntity` / `CameraInfo` / `Vector3` models + `AppCameraToModel` mapper
- _Tests: +5 (2 camera mapper, 3 mock-backed integration: subscribe/input/unsubscribe + rays broadcast)._

### Rendering layer (stretch — separate package)

- [x] `RustPlusApi.Camera` package (ns2.0;net10, ImageSharp 2.1.x, `2.0.0`) — separate so core stays image-free
- [x] `rayData` decode → image: faithful verbatim port of rustplus.js (`IndexGenerator` xorshift PRNG + seeded Fisher-Yates shuffle, the VLE+lookback ray decode, the 8-colour material palette + sky sentinel + Y-flip) in `CameraRenderer`
- [◐] Test-first against a captured fixture — **no real `AppCameraRays` capture exists yet (§15.4)**, so tests use _synthetic_ full-ray buffers to lock the decode math, sky sentinel, and material colouring (terrain ray → exact pixel). **End-to-end fidelity vs. a real server frame is unvalidated** and flagged experimental in the package README/XML docs
- [ ] Sample feature file (`samples/.../Features/`) — deferred to Phase 8 docs, added with clan/nexus samples together

---

## Phase 6 — Native credential acquisition (§7)

**Done when:** `RustPlus.Register.ConsoleApp` takes a user from zero → `rustplus.config.json` → a live `RustPlus` connection, once, by hand.

- [x] New package `RustPlusApi.Fcm.Registration` (ns2.0;net10, `2.0.0`)
- [x] Step 1–3: GCM check-in (code-first checkin protobuf), FIS, FCM register (`AndroidFcmRegister`, ported from `@liamcottle/push-receiver`)
- [x] Step 4: Expo push token (`ExpoPushClient`)
- [x] Step 5: Steam login (`SteamLoginService`) — launches Chrome/Chromium with the **DevTools protocol** and injects the `ReactNativeWebView.postMessage` shim via `Page.addScriptToEvaluateOnNewDocument` (Puppeteer's mechanism). Popup/opener injection and `--load-extension` are both blocked on modern Chrome (137+); CDP injection runs in the page's own context and **is validated working on real Chrome 149** by an opt-in canary. Native + Flatpak auto-detected; `CHROME_PATH` override
- [x] Step 6: register device with Rust Companion (`RustCompanionClient`)
- [x] Step 7: persist `Credentials` via `System.Text.Json` (`CredentialsStore`); `Credentials` extended (in `.Fcm`) with `Fcm`/`ExpoPushToken`
- [x] Convenience `PairingListener` — `Listening`/`Paired`/`Stopped`/`Failed` (modeled on rustplus-desktop's `IPairingListener`), `WaitForServerPairingAsync` → `ServerPairing`
- [x] `samples/RustPlus.Register.ConsoleApp` — orchestrates the full flow + writes `rustplus.config.json` + prints the `RustPlus(...)` args
- [x] Opt-in registration "canary" tests (`Canary/`, `[Fact(Skip)]` — not in the default gate; check-in canary validates the protobuf against the real Google endpoint when run manually)
- [x] Centralized Firebase/Expo constants in `RegistrationConstants` (read from rustplus.js, with the drift warning §15.3 mandates)
- [◐] **Live validation status:** steps 1–4 (check-in → Firebase → FCM → Expo) **verified against the real Google/Expo endpoints** by running the canary (returned valid androidId/securityToken/FCM/Expo tokens) — the fragile protobuf + constants are correct. Steps 5–8 (Chrome Steam login → Companion register → in-game pairing) are interactive and still need a manual run to confirm. Deterministic parts also unit-tested (checkin round-trip, FID, persistence, pairing mapping → 6 tests)

---

## Phase 7 — JSON cleanup (§8)

**Done when:** no bespoke converters remain except justified ones; all (de)serialization via STJ.

- [x] Inventory all serialization; standardize on `System.Text.Json` (the only bespoke converters were the two below; library is now STJ-only)
- [x] Deleted `Int32StringConverter` + `StringToUInt64Converter` (and the `Converters/` folder) — replaced by STJ's native `[JsonNumberHandling(AllowReadingFromString | WriteAsString)]` on `Body`. Covered by `FcmJsonTests` (read-from-string, write-as-string round-trip, numeric-json tolerance)
- [x] Credential model: stays in `.Fcm` (the listener needs it) with `.Registration` producing it — per the §15.2 decision; `CredentialsStore` (persistence) lives in `.Registration`

---

## Phase 8 — Docs & release (§12, §11)

**Done when:** three+ package READMEs packed; DocFX site builds; both packages tagged `2.0.0`; `MIGRATION.md` published.

- [x] Per-package READMEs — all four packed (verified `README.md` in each `.nupkg`); core README gained Clan/Nexus/Camera sections, Fcm README points at native registration
- [x] Trimmed root README — overview + package matrix + versions + quickstart + links (long walkthroughs moved to per-package READMEs)
- [x] DocFX site under `docs/` (`docfx.json` + `index.md` + `toc.yml`) + `docs.yml` GitHub Pages workflow — **builds locally: 212 pages, 0 warnings**; generated output gitignored
- [x] Replaced ".NET 8 or later" → ".NET Standard 2.0 / .NET 10" (root + per-package READMEs)
- [x] Documented camera/clan/nexus usage; native credential flow is the primary path (Node CLI a fallback note)
- [x] `MIGRATION.md` — legacy→modern mapping, protobuf-net serializer change, new TFMs, new packages, the IsError fix
- [x] Bumped `RustPlusApi` + `RustPlusApi.Fcm` `1.4.0` → `2.0.0`; new packages already `2.0.0`; `CD.yml` now packs all four
- _Note: DocFX/Pages publish + the actual NuGet push happen in CI on release; verified locally as far as possible (site builds, packages pack with READMEs)._

---

## Cross-cutting / standalone tasks

### Proto-refresh tooling (§6 — Method A)

**Done when:** `tools/update-proto/` runs end-to-end and reproduces the committed proto from a fresh server download. _Plan: [proto-refresh-plan.md](proto-refresh-plan.md)._

- [x] SteamCMD fetch of dedicated server (app `258550`) — `tools/update-proto/1-fetch-server.sh` (auto-installs SteamCMD, build-id capture). **Run end-to-end against server build `23601104`.**
- [x] Decompile contract types with `ilspycmd` — `2-decompile.sh` (auto-installs ilspycmd, `--list` discovery, pipefail-safe). **Run end-to-end.** Target corrected to `Rust.Data.dll`.
- [x] Regenerate `RustPlusContracts.proto` + diff — **⚠️ premise correction:** the server uses **SilentOrbit**, not protobuf-net (no `[ProtoContract]`, no protobuf-net dll), so `Serializer.GetProto<AppMessage>()` is **not applicable**. Built `ProtoGen` (Roslyn) which **parses the decompiled SilentOrbit C#** (field# from the dual `Deserialize` switch, type from decl + `ProtocolParser.Read*`, nesting from C# nested classes; labels/order/well-known-types preserved from committed). `update-proto.sh` runs the whole pipeline + diff gate. Regen reproduces the committed proto with a **25-line, all-genuine diff** (new fields incl. `time_of_day`; `Monument.name→token`; `entity_id` widening; `Unknow→Undefined`). _See [proto-refresh-plan.md](proto-refresh-plan.md) for the full result._
- [x] Document monthly (first-Thursday) rerun routine — cadence in `tools/update-proto/README.md`. _(Method D/B backups dropped: Method A is reliable. Optional scheduled Action not added — left as a documented option.)_
- [ ] **Follow-up:** apply the reviewed diff to `RustPlusContracts.proto` (one-time v2 refresh) + add `Data/*` mappers for any new fields, then PR.

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
