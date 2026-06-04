# RustPlusApi — v2 Modernization Plan

> Status: proposal / planning document
> Author: generated analysis, 2026-06-04
> Scope: `src/RustPlusApi` (core) and `src/RustPlusApi.Fcm` (FCM listener)

This document analyzes the current state of the repository and lays out a
detailed, step-by-step plan for the requested v2 work. The original five asks were:

1. Ship the NuGet packages for **both `netstandard2.0` and `net10`**.
2. Reduce to **a single Protobuf dependency**.
3. Move the proto definitions toward a **C# class (code-first) version**.
4. Replace the **hand-ported Node modules** with a C# equivalent where one exists.
5. **Drop everything tagged "legacy."**

Four further requirements were added and are now first-class deliverables:

6. A **fully native, end-to-end credential acquisition** flow (FCM **and** Rust+ /
   Steam) so the library is self-contained and testable without the Node CLI.
7. A **mock Rust+ server** so the library can be exercised without a real Facepunch
   game server (which is hard to stand up).
8. **Feature completeness** — the current wrapper omits the **camera system** and other
   capabilities; bring it in line with the latest Rust+ protocol and the reference
   projects [olijeffers0n/rustplus](https://github.com/olijeffers0n/rustplus) (Python)
   and [liamcottle/rustplus.js](https://github.com/liamcottle/rustplus.js) (Node).
9. **Code quality & SonarQube** — the code was written quickly; harden the reliability
   bugs, enforce the analyzers that are already configured, and make the Sonar quality
   gate actually gate (see §10).

Each section explains *what exists today*, *why it matters*, and *how to proceed*.

---

## 0. Current state (findings)

### Solution layout

| Project | Output | Protobuf lib | Proto schema |
|---|---|---|---|
| `src/RustPlusApi` | NuGet `RustPlusApi` (v1.4.0) | **Google.Protobuf** 3.35.0 | `Protobuf/RustPlusContracts.proto` (proto2, 45 messages) + checked-in `RustPlusContracts.cs` (~20.7k lines) |
| `src/RustPlusApi.Fcm` | NuGet `RustPlusApi.Fcm` (v1.4.0) | **protobuf-net** 3.2.56 | `ProtoBuf/mcs.proto` (proto2) + checked-in `Mcs.cs` |
| `samples/RustPlus.ConsoleApp` | sample | — | — |
| `samples/RustPlus.Fcm.ConsoleApp` | sample | — | — |

Key build facts:

- **`Directory.Build.props` forces `<TargetFramework>net10.0</TargetFramework>`** for
  every project (single-target, applied globally).
- **`Directory.Packages.props`** uses Central Package Management and pins **two**
  protobuf packages: `Google.Protobuf` and `protobuf-net`.
- Generated protobuf C# is **committed**, not generated at build time (no `Grpc.Tools`,
  no `protobuf-net.BuildTools`). The `.proto` files are kept only as source of truth.
- **CI/CD drift:** `CI.yml` and `CD.yml` install **`dotnet-version: 8.0.x`** but the
  projects target `net10.0`. The pipeline cannot build the repo as configured. The
  READMEs still say ".NET 8 or later."
- There is **no test project** (`dotnet test` in CI matches nothing).

### The protocol is *nearly* current — the wrapper lags badly

This is the most important finding for requirement #8.

There is **no official, public Rust+ `.proto`** — Facepunch never published one. Every
copy in the wild (yours included) is reverse-engineered from the Companion app by the
community, so "is mine current?" can only be answered by cross-checking the maintained
community copies. I did that:

- vs. [rustplus.js `rustplus.proto`](https://github.com/liamcottle/rustplus.js/blob/master/rustplus.proto)
  (Node): **identical** to your committed proto.
- vs. [olijeffers0n/rustplus `rustplus.proto`](https://github.com/olijeffers0n/rustplus/blob/master/rustplus.proto)
  (Python, actively maintained — last updated Dec 2025): **your proto has drifted
  behind.** Same 45 messages and identical request/response/broadcast *field numbers*
  (so still wire-compatible), but missing newer fields and using stale names:

  | Message | olijeffers0n (current) | your proto | impact |
  |---|---|---|---|
  | `AppInfo` | `bool camerasEnabled = 16` | absent | you can't tell if a server has cameras |
  | `AppCameraRays` | `float timeOfDay = 6` | absent | camera frames drop day/night info |
  | `AppMap.Monument` | `string token = 1` | `string name = 1` | Facepunch moved to a localization token |
  | `AppMap.Note` | `colourIndex = 6`, `label = 7` | `colour = 6`, `name = 7` | stale field names |

So the contract already includes the camera system, Nexus auth, and clan messaging — but
it needs a refresh against olijeffers0n, and (separately) the **wrapper never exposed**
camera/clan/nexus at all. Keeping the proto current going forward is its own problem; see
§6 ("Keeping the contract current").

The bigger gap is the **high-level `RustPlus` class** ([`src/RustPlusApi/RustPlus.cs`](src/RustPlusApi/RustPlus.cs)),
which wraps only a subset:

| Capability | In `.proto`? | In modern `RustPlus`? | In `RustPlusLegacy`? |
|---|---|---|---|
| Info / Time / Map / MapMarkers | ✅ | ✅ | ✅ |
| Team info / chat / send / promote | ✅ | ✅ | ✅ |
| Smart switch / storage monitor / alarm | ✅ | ✅ | ✅ (raw) |
| **Camera** (subscribe/input/rays) | ✅ | ❌ **missing entirely** | ❌ |
| **Clan** (info/motd/chat/send) | ✅ | ❌ **missing** | ✅ (raw only) |
| **Nexus auth** | ✅ | ❌ **missing** | ✅ (raw only) |
| Generic `GetEntityInfo` | ✅ | ❌ (typed only) | ✅ (raw) |

So "not fully completed / outdated" is accurate: camera/clan/nexus exist in the wire
contract but were never lifted into the typed `Response<T>` API. See §5.

### The "legacy" surface

- [`src/RustPlusApi/RustPlusLegacy.cs`](src/RustPlusApi/RustPlusLegacy.cs) — `[Obsolete]`
  class; its `*LegacyAsync` methods return raw `AppMessage`.
- `RustPlus.cs` is the modern `Response<T>` replacement.
- Both package READMEs document `RustPlusLegacy` heavily.

### The "Node module" lineage

The library re-implements [liamcottle/rustplus.js](https://github.com/liamcottle/rustplus.js).
The piece literally ported from Node is the **FCM / MCS push listener**, originally
[`MatthieuLemoine/push-receiver`](https://github.com/MatthieuLemoine/push-receiver):
`RustPlusFcmSocket.cs` (TLS socket to `mtalk.google.com:5228`, MCS framing, login,
heartbeat, data-message decode), `ProtoBuf/mcs.proto` + `Mcs.cs`, and `Utils/`+`Tags.cs`.

The credential **acquisition** step was *not* ported — the README offloads it to the
Node CLI. That is the gap requirement #6 closes.

### Cross-check: a real downstream consumer ([Pronwan/rustplus-desktop](https://github.com/Pronwan/rustplus-desktop))

This active C# WPF app (175★, updated daily) is a **real consumer of *this* library** —
`<PackageReference Include="RustPlusApi" Version="1.3.0" />`. Reading it both **validates
the plan** and supplies concrete API ideas:

- **Confirms the multi-target need (§1).** It targets **`net8.0-windows`**. If v2 shipped
  `net10`-only, this consumer would be stuck on 1.x. `netstandard2.0` keeps it (and every
  other net6/7/8 consumer) on the upgrade path — exactly the §1 rationale, now evidenced.
- **Confirms native credentials are worth it (§7).** To get credentials it **bundles an
  entire Node runtime (~2,800 files under `runtime/node-win-x64`)** and spawns the
  `rustplus.js` CLI (`fcm-register`/`fcm-listen`) as a background process, **regex-scraping
  its stdout** (`rustplus://…`, `{ key: 'gcm.notification.body', … }`) — fragile and heavy.
  A native C# registration package (§7) lets a consumer like this delete the Node bundle.
- **Validates the `IPairingListener` abstraction (§7) and the mock (§9).** It already has
  exactly this: an `IPairingListener` interface (events `Paired`, `Listening`,
  `RegistrationCompleted`, `Stopped`, `Failed`, `AlarmReceived`, `ChatReceived`) with two
  implementations — `PairingListenerRealProcess` (the Node process) and
  **`PairingListenerStub`** (commented *"placeholder… later replace with real Facepunch/FCM
  listener,"* Ctrl-P simulates a pairing). That's our `ICredentialProvider`/mock idea in the
  wild; adopt its event shape.
- **Validates the cross-platform Steam login (§7, step 5).** It ships a
  `SteamOpenIdLoopbackService` — a **loopback HTTP listener** capturing the Steam OpenID
  redirect — precisely the `HttpListener` approach we proposed (better than a Windows-only
  WebView2, which it also carries).
- **Validates the camera model (§5a).** Its `CameraFrame` record — `byte[] Bytes`, `Mime`,
  `Width`, `Height`, `IReadOnlyList<CameraEntity> Entities` (each with position + `Label` +
  `SteamId`) — mirrors our "decode rays → rendered frame + typed entities" split. Reuse
  this shape for `RustPlusApi.Camera`.

Net: an independent, active implementation arrived at the same abstractions we're planning,
and its pain points (Node bundle, stdout scraping) are exactly what v2 removes. The plan is
viable; treat rustplus-desktop as both a reference and a design partner / early adopter.

---

## 1. Multi-target: `netstandard2.0` + `net10` *(decided: netstandard2.0)*

### Why

Targeting only `net10.0` excludes .NET Framework 4.6.2+, older .NET Core / 5–9, Unity,
and Mono. `netstandard2.0` restores the widest reach; `net10.0` keeps modern BCL
fast-paths. Highest-value, lowest-risk change.

### Target set

```
netstandard2.0;net10.0
```

### How to proceed

1. **Stop forcing a single TFM globally.** In `Directory.Build.props` keep only
   framework-neutral settings:

   ```xml
   <Project>
     <PropertyGroup>
       <LangVersion>latest</LangVersion>
       <ImplicitUsings>enable</ImplicitUsings>
       <Nullable>enable</Nullable>
     </PropertyGroup>
   </Project>
   ```

   `LangVersion=latest` lets the modern syntax already in the code (primary
   constructors, collection expressions `[.. x]`, records) compile even when targeting
   `netstandard2.0` — those are compiler features, not runtime features.

2. **Set the multi-target per shipping library** (so samples stay net10-only). In
   `RustPlusApi.csproj` and `RustPlusApi.Fcm.csproj`:

   ```xml
   <TargetFrameworks>netstandard2.0;net10.0</TargetFrameworks>
   ```

3. **Close the `netstandard2.0` BCL gaps:**

   | API used | netstandard2.0 fix |
   |---|---|
   | `System.Text.Json` | add `System.Text.Json` package |
   | `ClientWebSocket` | add `System.Net.WebSockets.Client` if flagged |
   | `SslStream`, `TcpClient`, `BigInteger` | OK on ns2.0 |

   ```xml
   <ItemGroup Condition="'$(TargetFramework)' == 'netstandard2.0'">
     <PackageReference Include="System.Text.Json" />
   </ItemGroup>
   ```

   Add matching `<PackageVersion>` entries to `Directory.Packages.props`.

4. **Audit runtime-only APIs** (`DateTimeOffset.FromUnixTimeMilliseconds`,
   `Interlocked` on `uint`); wrap any gap behind `#if NET10_0_OR_GREATER`.

5. **Fix CI/CD** to install the .NET 10 SDK (§11) — it builds both TFMs.

### Acceptance

- `dotnet pack` yields a `.nupkg` with **both** `lib/netstandard2.0/` and `lib/net10.0/`.
- A .NET Framework 4.7.2 console app can reference the package and `new RustPlus(...)`.

---

## 2. One Protobuf dependency — **protobuf-net**

### The problem

Two protobuf stacks do the same job: `Google.Protobuf` (core) + `protobuf-net` (FCM),
with incompatible generated APIs and serializers.

### Why protobuf-net (not Google.Protobuf)

| Criterion | Google.Protobuf | protobuf-net |
|---|---|---|
| `netstandard2.0` (goal #1) | yes | **yes** |
| proto2 (both schemas are proto2) | C# historically proto3-only | **first-class proto2** |
| Code-first C# classes (goal #3) | **not supported** | **supported** |
| Already in repo | core only | **FCM already uses it** |
| Build-time gen | `Grpc.Tools` | `protobuf-net.BuildTools` / `protogen` |

Standardizing on Google.Protobuf would make goal #3 impossible (no code-first mode), so
the choice is effectively forced by your own requirements.

### How to proceed

1. **Convert `RustPlusContracts.proto` to protobuf-net classes** with `protogen`:

   ```bash
   dotnet tool install --global protobuf-net.Protogen
   protogen --csharp_out=. Protobuf/RustPlusContracts.proto
   ```

   Produces protobuf-net-style C# (same shape as the existing `Mcs.cs`), replacing the
   Google-generated `RustPlusContracts.cs`.

2. **Swap the serializer calls in `RustPlusSocket.cs`:**

   | Google.Protobuf (today) | protobuf-net (after) |
   |---|---|
   | `AppMessage.Parser.ParseFrom(bytes)` | `Serializer.Deserialize<AppMessage>(stream)` |
   | `request.ToByteArray()` | `Serializer.Serialize(ms, request); ms.ToArray()` |
   | presence via `X is not null` | protobuf-net nullable / `ShouldSerializeX()` |

   **Re-validate every presence check.** `RustPlusSocket.IsError` and the `Extensions/*`
   mappers rely on `message.Response.Success is not null` semantics; protobuf-net models
   "field set" via nullable backing fields + `ShouldSerialize*`. This is the subtle part.

3. **Delete `Google.Protobuf`** from `Directory.Packages.props` and the core csproj; add
   `protobuf-net` there.

4. **Regression-test the wire format** with captured `AppMessage` fixtures (§9).

### Risk note

Highest-risk item — it rewrites the core serialization path. Sequence it **after** the
test project + mock server exist (§9) and **before** code-first migration (§3).

---

## 3. Code-first C# protos

### What it means

protobuf-net describes messages as plain C# classes with `[ProtoContract]` /
`[ProtoMember(n)]` — no `.proto`, no codegen. Available for both schemas once §2 lands.

### Recommendation (decided: no preference → optimize per schema)

- **MCS (`mcs.proto`) → hand-written code-first C# classes.** Small (16 messages),
  frozen Chromium protocol. Author `[ProtoContract]` types under
  `src/RustPlusApi.Fcm/ProtoBuf/`, keep namespace `McsProto`, delete `mcs.proto` + `Mcs.cs`.

  ```csharp
  [ProtoContract]
  public sealed class HeartbeatPing
  {
      [ProtoMember(1)] public int? StreamId { get; set; }
      [ProtoMember(2)] public int? LastStreamIdReceived { get; set; }
      [ProtoMember(3)] public long? Status { get; set; }
  }
  ```

- **RustPlusContracts → build-time gen from `.proto`** via `protobuf-net.BuildTools`.
  This schema tracks the Rust+ app and changes upstream; keeping the `.proto` as source
  means re-syncing is "drop in a new file," not "hand-port 45 messages." Also removes
  the giant committed `.cs` from review.

  ```xml
  <ItemGroup>
    <PackageReference Include="protobuf-net.BuildTools" PrivateAssets="all" />
    <Protobuf Include="Protobuf/RustPlusContracts.proto" />
  </ItemGroup>
  ```

> If build-time gen is fiddly across both TFMs, fall back to committing `protogen`
> output (status quo) — same result, manual regen step.

---

## 4. Drop everything tagged "legacy"

### Inventory

- [`RustPlusLegacy.cs`](src/RustPlusApi/RustPlusLegacy.cs) — the whole `[Obsolete]` class
  and all `*LegacyAsync` methods returning raw `AppMessage`.
- README sections documenting it.
- Any sample/feature code calling `*Legacy*`.

### Why now (before the serializer swap)

`RustPlusLegacy` exposes the raw `AppMessage` type in its public signatures. If kept,
every `*LegacyAsync` signature changes when `AppMessage` moves from Google.Protobuf to
protobuf-net — a breaking change you'd ship then immediately delete. Remove legacy first
to shrink the §2 migration surface.

### How to proceed

1. **Map each legacy method to a modern equivalent first.** Several legacy-only
   capabilities (clan, nexus, generic entity info) are *not yet* in `RustPlus` — those
   move to §5 (feature completeness), not deletion. Don't drop a capability that has no
   modern replacement; lift it first.
2. Delete `RustPlusLegacy.cs`.
3. `grep -ri legacy src samples` and clean every call site; ensure no public method
   returns a raw `AppMessage`.
4. Strip legacy sections from the READMEs (§12).
5. Breaking change → bump both packages to **2.0.0** (§11).

---

## 5. Feature completeness (camera, clan, nexus) — latest protocol

### Findings

The `.proto` is already current; the typed wrapper just never exposed three families.
Bring them into the modern `RustPlus` API (typed `Response<T>` + events), matching the
behavior of the reference projects.

#### 5a. Camera system (the big one)

The Rust+ camera/CCTV system (cameras, PTZ, drones, auto-turrets) works like this:

- `cameraSubscribe(AppCameraSubscribe{ cameraId })` → server replies with
  `AppCameraInfo{ width, height, nearPlane, farPlane, controlFlags }`.
- The server then streams `AppCameraRays` **broadcasts**: `verticalFov`, `sampleOffset`,
  `rayData` (a run-length-encoded depth + entity-sample buffer), `distance`, and an
  `entities` list (players/objects in view with positions).
- `cameraInput(AppCameraInput{ buttons, mouseDelta })` drives movement/zoom/fire.
- `cameraUnsubscribe` ends the stream.

Both references implement two layers; mirror that split:

1. **Protocol layer (must-have).** `SubscribeToCameraAsync(string cameraId)`,
   `SendCameraInputAsync(...)`, `UnsubscribeFromCameraAsync()`, plus an
   `OnCameraRaysReceived` event carrying a typed `CameraFrame` (the decoded ray samples
   + entities). Model `controlFlags`/`buttons` as a `[Flags]` enum.
2. **Rendering layer (stretch, optional package).** Decode `rayData` → a 2D image, as
   rustplus.js (`Camera.js`, canvas) and olijeffers0n (`RustCamera`, PIL) do. Image
   encoding needs an imaging lib; **do not force that dependency on every consumer** —
   ship rendering as a separate package `RustPlusApi.Camera` that depends on
   `SixLabors.ImageSharp` (netstandard2.0-compatible) and turns a `CameraFrame` into a
   `byte[]`/`Image`. Core stays image-free.

This is the single largest feature; treat it as its own milestone and lean on the mock
server (§9) to develop it — camera frames are otherwise impossible to test offline. For
the `CameraFrame`/`CameraEntity` shape, reuse rustplus-desktop's proven model (§0
cross-check): `byte[] Bytes` + `Mime` + `Width`/`Height` + a typed entity list.

#### 5b. Clan

Add typed wrappers + models for `getClanInfo`, `setClanMotd`, `getClanChat`,
`sendClanMessage`, and the `AppClanChanged` / `AppNewClanMessage` broadcasts
(`OnClanChatReceived`, `OnClanChanged` events). Today these exist only as raw legacy
methods; port them to `Response<T>` before deleting legacy (§4).

#### 5c. Nexus auth

Add `GetNexusAuthAsync(string appKey)` returning a typed `Response<NexusAuth>`.

#### 5d. Audit against the references for drift

Walk the request/response surface in olijeffers0n/rustplus and rustplus.js and confirm
nothing else lags (e.g. subscriber-info helpers, marker types like vending machines,
`AppMarker`/`AppMapMarkers` completeness). The wire types are present; verify the
**mappers** in `Extensions/*` cover every field the latest app sends.

### How to proceed

0. **Refresh the proto first** via **Method A (§6)** — decompile the dedicated server for
   the authoritative, current contracts (this is where `AppInfo.camerasEnabled`,
   `AppCameraRays.timeOfDay`, and the real `Monument`/`Note` field names come from).
   **Keep** your richer proto2 enum typing (`IconType`/`IconColor`) where it adds value.
   Do this before generating the camera models so they include `timeOfDay`/`camerasEnabled`.
   (The §0 deltas are the *minimum* known gap; Method A may surface more.)
1. Generate the typed `Data/*` models for camera frame, clan, nexus (mirror existing
   `Data/` conventions).
2. Add the methods/events to `RustPlus.cs`; add mappers in `Extensions/`.
3. Add sample feature files under `samples/RustPlus.ConsoleApp/Features/`.
4. Cover each with mock-server-backed tests (§9).

---

## 6. Keeping the contract current (there is no official `.proto`)

You said it yourself: there is **no public Rust+ schema** — you inherited a proto that
others retro-engineered from the Companion app, and the §0 diff proves the cost: your
copy silently fell behind by two fields and three renames. The community copies are
**not a reliable signal** either — they're updated sporadically, so "wait for
olijeffers0n to update" can leave you behind for months. The answer is to derive the
schema **yourself, authoritatively, on Rust's release cadence**.

### Method A — Decompile the Rust **dedicated server** *(primary — documented & scripted in-repo)*

Rust is a Unity/C# game and **Facepunch uses protobuf-net internally**. The Companion
contracts (`AppRequest`, `AppMessage`, `AppCameraInput`, …) are compiled as
`[ProtoContract]` classes straight into the server assemblies — so the dedicated server
**is the source of truth**, it's free, and the whole extraction can be scripted and
committed to the repo for reproducible reruns.

**Reproducible pipeline** (store under `tools/update-proto/`):

1. **Fetch/update the server** headlessly via SteamCMD (no game license needed):
   `steamcmd +force_install_dir ./rds +login anonymous +app_update 258550 validate +quit`.
2. **Decompile the contract types** with the CLI decompiler `ilspycmd`
   (`dotnet tool install -g ilspycmd`), pointed at
   `rds/RustDedicated_Data/Managed/Assembly-CSharp.dll` and the `ProtoBuf`/companion
   namespace where `AppRequest` & friends live. The `[ProtoMember(n)]` attributes give
   exact field numbers, types, and names — including anything not yet in the community protos.
3. **Emit a fresh `.proto`** (or lift the classes directly). Because v2 standardizes on
   protobuf-net (§2/§3), the decompiled types are *already in your target format*; a tiny
   tool that calls `Serializer.GetProto<AppMessage>()` regenerates `RustPlusContracts.proto`
   from them. Diff against the committed proto and open a PR.

This synergy — the source of truth is itself protobuf-net — is the single strongest
argument for the protobuf-net choice (§2).

**Run it on Rust's release cadence.** Rust ships a forced update on the **first Thursday
of each month (~18:00 UTC / 20:00 UTC+2)**. That is the moment the contracts can change,
so the documented routine is: after the monthly update lands, re-run `tools/update-proto/`,
review the diff, and ship any delta. (A scheduled Action can *trigger the rerun* on that
date — but it watches the authoritative server, not the community libs.)

### Method D — Live capture *(backup validation, if A fails)*

If Method A is ever blocked (e.g. obfuscation changes, decompiler breakage), fall back to
verifying against **real traffic**: MITM the WebSocket (proxy `wss://companion-rust.facepunch.com`)
and run captured payloads through `protoc --decode_raw`. This confirms field numbers and
wire types and catches silent changes — but it **cannot recover field names**, so it's a
validation/early-warning backstop, not a primary discovery method. Keep a short runbook
for it in `tools/` so it's ready when needed.

### Method B — Extract from the Companion **mobile app** *(last resort, only if A is impossible)*

This is how the community derived their protos, but it's redundant once A works — only
reach for it if the dedicated server route becomes impossible. The Rust+ app is a React
Native / Expo app using protobufjs, which embeds the schema: unzip the APK, find the JS
in `assets/index.android.bundle` (disassemble with `hermes-dec`/`hbctool` if it's Hermes
bytecode), and grep for the protobuf descriptor (`"AppRequest"`, `"cameraSubscribe"`),
which protobufjs stores as a `.proto` string or reconstructable JSON. iOS IPA is analogous.

### Not recommended — drift-watch against the community libs

Diffing against olijeffers0n/liamcottle (the cross-check I ran in §0) is fine as a
*one-off sanity check*, but **not as the ongoing signal**: those projects update
infrequently, so they lag the game and would give false confidence. Method A on the
monthly cadence replaces it.

### Recommendation

- **One-time for v2:** run **Method A** to refresh the proto authoritatively (it resolves
  the `Monument.token` / `Note` naming with Facepunch's real names and picks up
  `camerasEnabled` / `timeOfDay`). Commit the `tools/update-proto/` script alongside it.
- **Ongoing:** re-run the script after each **first-Thursday monthly update**; keep
  **Method D** as the documented backup and **Method B** as the break-glass last resort.

---

## 7. Native credential acquisition (FCM **and** Rust+) — the self-contained flow

This is requirement #6 and the prerequisite for *you* being able to test the library.
Goal: a user runs one C# tool, logs into Steam once, pairs in-game, and gets everything
`new RustPlus(server, port, playerId, playerToken)` and `new RustPlusFcm(credentials)`
need — **no Node, no `rustplus.config.json` from another project.**

### The full chain (what "all the credentials" actually entails)

Confirmed against rustplus.js / `@liamcottle/push-receiver` and the
[Rust+ pairing flow doc](https://github.com/liamcottle/rustplus.js/blob/master/docs/PairingFlow.md):

| # | Step | Endpoint (current; mirror push-receiver) | Produces |
|---|---|---|---|
| 1 | **GCM check-in** | `POST https://android.clients.google.com/checkin` (protobuf) | `androidId` + `securityToken` → these are the `Credentials.Gcm` the **MCS listener logs in with** |
| 2 | **Firebase installation (FIS)** | `POST https://firebaseinstallations.googleapis.com/v1/projects/{firebaseProjectId}/installations` (header `x-goog-api-key`) | FID + installation `authToken` |
| 3 | **FCM register** | `POST https://fcm.googleapis.com/fcm/connect/register` (newer) using FIS token + GCM identity | **FCM token** |
| 4 | **Expo push token** | `POST https://exp.host/--/api/v2/push/getExpoPushToken` with `{ deviceId, experienceId: '@facepunch/RustCompanion', appId, deviceToken: <fcmToken>, type: 'fcm' }` | `ExponentPushToken[...]` |
| 5 | **Steam login (interactive)** | open `https://companion-rust.facepunch.com/login` in browser; Steam OpenID redirects back with the token | **Steam auth token** |
| 6 | **Register device with Rust Companion** | `POST https://companion-rust.facepunch.com/api/push/register` with `{ AuthToken: <steam>, DeviceId, PushKind: 3, PushToken: <expoToken> }` | device subscribed to pairing pushes |
| 7 | **Persist credentials** | — | the `Credentials` blob (gcm id/token, fcm token, expo token, keys) — the `rustplus.config.json` equivalent, consumed by `RustPlusFcm` |
| 8 | **Pair in-game** (runtime) | start `RustPlusFcm.ConnectAsync()`; in game choose *Pair with Server* / *Pair Smart Device* | FCM notification whose body carries `ip`, `port`, `playerId`, **`playerToken`**, `name`, `id`, … and for entities `entityId`/`entityType` |

Steps 1–7 run **once** (the "register" tool). Step 8 is already implemented — the
existing `RustPlusFcmSocket.OnDataMessage` decodes exactly that pairing body. The four
fields from step 8 (`ip`/`port`/`playerId`/`playerToken`) are precisely the `RustPlus`
constructor args. **That closes the loop end-to-end in C#.**

### Design

- New package **`RustPlusApi.Fcm.Registration`** (keeps the imaging/HTTP/registration
  concerns out of the lean listener package). Public surface:
  - `FcmRegistration.RegisterAsync(...)` → steps 1–4 → returns `Credentials` + Expo token.
  - `RustPlusRegistration.LinkSteamAsync(...)` → step 5 via a local `HttpListener` +
    `Process.Start(browserUrl)`; captures the redirect token. (Steam OpenID is
    interactive — there is no headless path; the user must click "Sign in through Steam.")
  - `RustPlusRegistration.RegisterWithCompanionAsync(steamToken, expoToken)` → step 6.
  - A convenience `PairingListener` wrapping `RustPlusFcm` to surface the first
    server-pairing notification as a strongly-typed `ServerPairing` (ip/port/playerId/
    playerToken), so the whole flow is one `await`. Model the event surface on
    rustplus-desktop's `IPairingListener` (§0 cross-check) — `Paired`, `Listening`,
    `RegistrationCompleted`, `Stopped`, `Failed` — so it can drop in as their
    `PairingListenerRealProcess` replacement and delete the Node bundle. For step 5,
    their `SteamOpenIdLoopbackService` confirms the loopback-`HttpListener` route works
    cross-platform.
- New sample **`samples/RustPlus.Register.ConsoleApp`** — the C# analog of
  `rustplus.js fcm-register`: run it, log into Steam in the browser, pair in game, and it
  writes `rustplus.config.json` + prints the `RustPlus(...)` args.

### Important caveat — this is the most upstream-fragile code in the repo

Google changed FCM/GCM registration in 2023–2024 and broke many clients; the exact
endpoints in steps 1–3 shift over time. Mitigations to bake into the plan:

- **Mirror the *current* `@liamcottle/push-receiver`** implementation rather than older
  docs; pin the Rust+ Firebase/Expo constants (project id, API key, sender id, app id)
  from the live app and keep them in one constants file.
- Add an **integration "canary" test** (opt-in, not in the default CI gate) that runs the
  registration handshake against the real endpoints, so breakage is detected early
  rather than by users.
- Document clearly that registration depends on Google/Facepunch services and may need
  periodic updates — unlike the rest of the library, which is self-contained.

### How to proceed

1. Port GCM check-in protobuf messages (reuse protobuf-net) + the check-in/register HTTP.
2. Implement FIS + FCM register (steps 2–3); implement Expo token (step 4).
3. Implement the `HttpListener` Steam-login capture (step 5) and Companion register (6).
4. Persist/serialize `Credentials` with `System.Text.Json`.
5. Wire the convenience `PairingListener`; build the `RustPlus.Register.ConsoleApp`.
6. Update the READMEs to make this the primary path; keep the Node CLI as a fallback note.

---

## 8. Finish the Node-module cleanup (JSON)

The MCS listener itself stays (it *is* the C# equivalent; no maintained C# NuGet replaces
an MCS receiver — `web-push-csharp` is send-side only). The remaining Node-ism is JSON
glue: hand-rolled converters (`Converters/Int32StringConverter.cs`,
`StringToUInt64Converter.cs`) and ad-hoc parsing.

- Inventory all serialization; standardize on **`System.Text.Json`** (already in use).
- Delete converters that STJ handles natively; keep only the genuinely-needed ones.
- Isolate the credential model behind the registration package (§7) rather than scattered
  `Data` types.

---

## 9. Mock Rust+ server + test strategy

### Why this is high-value

Requirement #7: a real Rust (Facepunch) game server is hard to stand up, so almost
nothing in the library is testable end-to-end today. A **mock server** unlocks: the §2
serializer swap, the §5 camera/clan features (camera frames are otherwise impossible to
observe offline), and regression safety for everything else. rustplus-desktop reached for
the same idea at the *pairing* layer with its `PairingListenerStub` (§0 cross-check) — we
generalize it to the whole protocol so it can back automated tests, not just manual Ctrl-P.

### Design — `test/RustPlusApi.MockServer`

- A small **WebSocket server** (`HttpListener`/Kestrel/`System.Net.WebSockets`) that
  speaks the real wire protocol: accepts an `AppRequest`, matches on which oneof field is
  set, and replies with a canned `AppMessage` (serialized with the *same* protobuf-net
  types the library uses). Because it shares the contract assembly, request/response stay
  in lockstep with the schema.
- **Scripted scenarios**: a fixture set of realistic responses (server info, map markers,
  team info, entity changed broadcasts, **camera info + a sequence of `AppCameraRays`
  frames**, clan chat). Capture a few real payloads once (hex/base64) and replay them.
- **Broadcast injection**: the mock can push `AppBroadcast` messages on demand to test
  `OnSmartSwitchTriggered`, `OnTeamChatReceived`, `OnClanChatReceived`, camera streams.
- Optional: a tiny **mock FCM data-message emitter** to test `RustPlusFcm` pairing
  parsing without Google (feed a crafted `DataMessageStanza` body through the same
  `OnDataMessage` path).

### Test project — `test/RustPlusApi.Tests` (xUnit)

- **Unit**: protobuf round-trip fixtures for `AppMessage` (guards §2); MCS framing/varint
  (guards §3); `Extensions/*` mapper assertions (guards §5).
- **Integration**: spin up `MockServer`, point a real `RustPlus` client at
  `ws://localhost:{port}`, and assert each `*Async` returns the expected `Response<T>`
  and each event fires. This is the harness you'll actually use to develop the camera
  feature.
- Target the test project at `net10.0`; reference libraries by project reference. Add a
  second TFM run (or a CI matrix leg) that exercises the `netstandard2.0` build of the
  libraries via a net10 host.
- Keep the §7 registration **canary** integration test in a separate, opt-in category
  (hits live Google/Facepunch endpoints — not part of the default gate).

---

## 10. Code quality & SonarQube

A stated v2 goal: the code was written quickly and needs a quality pass. There's already a
foundation to build on — don't start from scratch, *enforce* what's half-configured.

### Current state

- **`.editorconfig` is already strong** — every analyzer category (`Reliability`,
  `Design`, `Performance`, `Usage`, …) is set to `warning`, with a curated list of
  silenced rules. Good. **But warnings don't fail the build**, so they accumulate unseen.
- **`Sonar.yml` exists but under-delivers**: it runs **only on push to `develop`** (not on
  PRs, so nothing is gated before merge), excludes `samples/`, and runs a bare
  `dotnet build` — **no test run, so SonarQube imports 0% coverage**.
- **No test project** → coverage is 0; any coverage condition in the quality gate is red.

### Concrete smells to fix (grounded in the current code, not generic)

These are real issues spotted while reading the source — each maps to a Sonar/Roslyn rule:

| # | Issue | Where | Rule(s) |
|---|---|---|---|
| 1 | **`Dispose()` is a no-op** — only calls `GC.SuppressFinalize(this)`; never disposes the socket, `SslStream`, `TcpClient`, `ClientWebSocket`, or the `CancellationTokenSource`. Resource leak. | `RustPlusSocket.Dispose`, `RustPlusFcmSocket.Dispose` | S2931 / S3881, CA1816, CA2213 |
| 2 | **Fire-and-forget async** — `Task.Delay(1000).ContinueWith(async _ => …)` (the async lambda is never awaited) with "Not sure about that" / "For some reason, I have to wait" comments. | `RustPlusSocket.DisconnectAsync` | S3168, CA2008 |
| 3 | **`Console.WriteLine` inside a library** (`"⚠️ No AppData…"`). Libraries must not write to stdout; raise an event/log. | `RustPlusFcmSocket.OnDataMessage` | S106 |
| 4 | **Throwing base `Exception`** with `#pragma warning disable CA2201`. | `RustPlusFcmSocket.ReceiveMessages` | CA2201, S112 |
| 5 | **Throwing inside the receive loop** on an unknown tag (`ArgumentOutOfRangeException`) can kill the background task instead of degrading. | `RustPlusFcmSocket.OnMessage` | reliability |
| 6 | **Busy-poll loops** (`Task.Delay(100)` send pump; `while/Task.Delay(50)` disconnect wait). | `ProcessSendQueueAsync`, `DisconnectAsync` | maintainability/perf |
| 7 | **Response correlation by FIFO order**, not by `seq` — fragile if ordering assumptions break. | `_responseQueue` in `RustPlusSocket` | reliability |
| 8 | **`EncodeVarInt32(0)` returns an empty array** (loop never runs for 0). Latent encoding bug. | `Fcm/Utils/Utils.cs` | bug |
| 9 | **Missing `ConfigureAwait(false)`** on library awaits — deadlock risk in sync/UI contexts (more relevant once netstandard2.0 consumers like rustplus-desktop's WPF app are in scope). | all `await`s in both sockets | CA2007 |
| 10 | **Type named `Utils` in namespace `…Utils`** + commented-out code / `TODO` blocks. | `Fcm/Utils/Utils.cs`, `RustPlusSocket` TODOs | CA1724, S125 |

### How to proceed

1. **Make the analyzers bite.** In `Directory.Build.props`, turn on
   `<EnableNETAnalyzers>true</EnableNETAnalyzers>`,
   `<AnalysisLevel>latest-Recommended</AnalysisLevel>`, and add the
   **`SonarAnalyzer.CSharp`** package (PrivateAssets=all) so you get the *same* rules
   locally that the server applies. Promote a curated critical set to errors via
   `<WarningsAsErrors>` (e.g. CA2213, CA2007, S2931, S3881, S2221) rather than a blanket
   `TreatWarningsAsErrors` (which would drown you in style noise on day one).
2. **Fix the reliability bugs as an explicit early hardening pass** — items 1–5 and 8 are
   correctness/leak issues, not cosmetics. They also overlap files you touch in Phase 2
   (serializer) and Phase 4 (multi-target), so fold them into those edits, but treat the
   `Dispose`/async/`Console` trio as a named task, not opportunistic cleanup.
3. **Wire coverage into Sonar.** Run `dotnet test --collect:"XPlat Code Coverage"` (coverlet)
   and pass `/d:sonar.cs.opencover.reportsPaths=…` to the scanner `begin` step, so the
   gate sees real coverage from the §9 mock-backed tests.
4. **Gate on PRs, not just `develop`.** Add `pull_request` to `Sonar.yml`'s triggers and
   make the **SonarQube Quality Gate a required status check** on the default branch.
   Adopt **"Clean as You Code"**: 0 new bugs/vulnerabilities, no new code smells above
   severity X, and coverage-on-new-code ≥ a target (e.g. 70–80%) — applied to *changed*
   code so you're not blocked on legacy debt while still ratcheting quality up.
5. **Burn down legacy debt opportunistically.** Don't gate the whole release on zeroing the
   existing Sonar backlog; let "Clean as You Code" + the per-phase file touches retire it
   as the rewrite progresses.

> Synergy: dropping legacy (§4) and the big committed `RustPlusContracts.cs` (§3) alone
> removes a large chunk of Sonar's reported lines/smells; the protobuf-net + code-first
> moves shrink the surface the gate has to police.

---

## 11. CI/CD & versioning

### CI/CD SDK fix (blocking)

- Bump `actions/setup-dotnet` to install **`10.0.x`** in both `CI.yml` and `CD.yml`
  (today they install 8.0.x against a net10 target — the pipeline can't build).
- `dotnet pack` produces both `lib/netstandard2.0` and `lib/net10.0` unchanged.
- Add a `dotnet test` step that actually has a project to run (§9), and a separate
  workflow (or job filter) for the opt-in registration canary.

### Versioning

- Breaking release (dropped legacy, swapped serializer, new TFMs, new APIs) → move both
  packages `1.4.0` → **`2.0.0`**. Update `<Version>`, README install snippets, badges.
- New packages `RustPlusApi.Fcm.Registration` and `RustPlusApi.Camera` ship at `2.0.0`
  alongside the core (all v2 work targets the 2.0 release — no follow-up split).

---

## 12. Documentation — per-package READMEs + DocFX *(decided)*

Decision: **keep a README per NuGet** (packed with each package), **reduce the root
README**, and surface the long-form content through **DocFX**.

### Structure

- `src/RustPlusApi/README.md` — concise, package-scoped; packed via the existing
  `PackageReadmeFile`. Same for `src/RustPlusApi.Fcm/README.md` and the two new packages
  (`RustPlusApi.Fcm.Registration`, `RustPlusApi.Camera`).
- Root `README.md` — trimmed to: what the project is, the package matrix + badges, a
  quickstart, and links into the DocFX site. Remove the long usage walkthroughs.
- **DocFX site** (`docs/`) — the home for the full content: the end-to-end credential
  flow (§7) with diagrams, per-feature guides (camera, clan, team), the API reference
  (DocFX generates it from XML doc comments — which the codebase already has, richly),
  and the mock-server testing guide. Publish via GitHub Pages from a `docs.yml` workflow.

### Content updates regardless of layout

1. Replace ".NET 8 or later" with ".NET Standard 2.0+ / .NET 10".
2. Delete all `RustPlusLegacy` sections (§4).
3. State the single protobuf dependency (protobuf-net) and code-first MCS.
4. Make the **native** credential flow (§7) the primary path; Node CLI becomes a
   fallback footnote. Add the "this depends on Google/Facepunch and may need updates" note.
5. Document camera/clan/nexus usage (§5).

> DocFX consideration: DocFX's API reference works best with a consistent target; point
> its metadata step at the `net10.0` build to avoid multi-TFM duplication in the generated
> reference.

---

## 13. Suggested execution order

1. **Phase 0 — Unblock & safety net**
   - Fix CI/CD SDK to .NET 10 (§11); enable analyzers-as-errors + SonarAnalyzer and wire
     coverage + PR quality gate (§10).
   - Build the **mock server** + test project with protobuf/MCS fixtures (§9). *Do this
     early — it gates everything risky that follows.*
   - **Reliability hardening pass** (§10 items 1–5, 8): fix `Dispose`, the fire-and-forget
     async, library `Console.WriteLine`, and the varint-zero bug before refactoring on top.
2. **Phase 1 — Feature parity prep + drop legacy** (§4, §5b/§5c)
   - Port clan + nexus from legacy into modern `RustPlus` (typed), then delete
     `RustPlusLegacy`. (Lift before drop, so no capability is lost.)
3. **Phase 2 — Single Protobuf dependency** (§2) — gated by Phase 0 tests.
4. **Phase 3 — Code-first protos** (§3).
5. **Phase 4 — Multi-target** `netstandard2.0;net10.0` (§1).
6. **Phase 5 — Camera system** (§5a) — protocol layer in core; optional
   `RustPlusApi.Camera` rendering package. Developed against the mock server.
7. **Phase 6 — Native credentials** (§7) — `RustPlusApi.Fcm.Registration` + register
   sample + canary test.
8. **Phase 7 — JSON cleanup** (§8).
9. **Phase 8 — Docs & release** (§12) — per-package READMEs, DocFX site, bump to `2.0.0`.

> **Everything above ships in the single `2.0.0` release** — there is no 2.1 split. The
> phases are an ordering for the work, not a release boundary: Phases 0–4 are the
> modernization core; the camera system (§5a, incl. the separate `RustPlusApi.Camera`
> rendering package), native credentials (§7), and docs all land in the same 2.0 cut.
> If schedule pressure appears, the natural thing to *narrow* (not defer to 2.1) is the
> camera **rendering** layer — the camera **protocol** layer stays in 2.0 regardless.
>
> Code quality (§10) is **cross-cutting, not a phase**: the reliability fixes sit in
> Phase 0, and "Clean as You Code" applies to every phase's diffs. The Sonar gate ratchets
> quality up as the rewrite proceeds rather than being a single end-of-line task.

---

## 14. Resolved decisions

| # | Decision | Resolution |
|---|---|---|
| 1 | netstandard floor | **`netstandard2.0`** (reaches .NET Framework). |
| 2 | RustPlusContracts codegen | No preference → **build-time gen** via `protobuf-net.BuildTools` (fallback: commit `protogen` output). |
| 3 | Native FCM registration scope | **In scope** — needed for testing; full flow in §7, targeted for 2.0. |
| 4 | README layout | **Per-package READMEs** (packed) + **reduced root README** + **DocFX** site (§12). |
| 5 | Release scope | **Single `2.0.0`** — all work (core, camera, registration, docs) ships together; no 2.1 split. |
| 6 | Camera rendering package | **Separate `RustPlusApi.Camera`** (ImageSharp) so the core stays dependency-light. |
| 7 | Keeping the proto current | **No official schema exists.** Primary = **decompile the Rust dedicated server** (Method A, §6), scripted in `tools/update-proto/` and re-run after each first-Thursday monthly update. Method D (live capture) is the backup; Method B (APK) the last resort; community-lib drift-watch rejected (they lag). |
| 8 | Code quality enforcement | Build on the existing `.editorconfig`/`Sonar.yml`: **curated `WarningsAsErrors` + `SonarAnalyzer.CSharp`**, coverage wired into Sonar, **PR-gated "Clean as You Code"** (not a big-bang legacy cleanup). Reliability bugs fixed in Phase 0 (§10). |

---

## 15. Implementation readiness — gaps to close before/while coding

The plan above is structurally complete and can be followed phase-by-phase. These are the
concrete "fill-in" items a developer will hit; close each as you reach its phase so the
plan is self-contained rather than research-as-you-go.

### 15.1 Definition of Done per phase

Make each phase shippable on its own with an explicit check:

| Phase | Done when |
|---|---|
| 0 Safety net | CI builds on .NET 10; `dotnet test` runs ≥1 real test; mock server accepts a connection and replays one canned `AppMessage`; **SonarAnalyzer + curated `WarningsAsErrors` on, coverage imported, Sonar gate required on PRs (§10); reliability bugs §10.1–5,8 fixed.** |
| 1 Parity + drop legacy | Clan + nexus exposed as `Response<T>`; `RustPlusLegacy.cs` deleted; `grep -ri legacy src samples` empty; no public API returns raw `AppMessage`. |
| 2 Single protobuf | `Google.Protobuf` removed from `Directory.Packages.props`; all round-trip fixtures green; every former `is null` presence check re-verified. |
| 3 Code-first protos | `mcs.proto`/`Mcs.cs` gone (hand-written types); `RustPlusContracts.cs` no longer committed (build-gen) or regenerated via protogen; build clean on both TFMs. |
| 4 Multi-target | `dotnet pack` emits `lib/netstandard2.0` + `lib/net10.0`; a net48 smoke app references the package and constructs `RustPlus`. |
| 5 Camera | subscribe/input/unsubscribe + `OnCameraRaysReceived` work against the mock; `RustPlusApi.Camera` renders a fixture frame to a non-empty image. |
| 6/§6 Proto refresh | `tools/update-proto/` runs end-to-end and reproduces the committed proto from a fresh server download. |
| 7 Credentials | `RustPlus.Register.ConsoleApp` takes a user from zero → `rustplus.config.json` → a live `RustPlus` connection, once, by hand. |
| 8 JSON | no bespoke converters remain except justified ones; all (de)serialization via STJ. |
| 9 Docs/release | three+ package READMEs packed; DocFX site builds; both packages tagged `2.0.0`; `MIGRATION.md` published (15.5). |

### 15.2 Package dependency graph (decide names/refs up front)

```text
RustPlusApi            (core; protobuf-net)            -- netstandard2.0;net10
 +- RustPlusApi.Camera (depends on core + ImageSharp)  -- rendering only
RustPlusApi.Fcm        (MCS listener; protobuf-net)
 +- RustPlusApi.Fcm.Registration (depends on Fcm)      -- GCM/FIS/FCM/Expo/Steam/Companion
```

Confirm whether `Credentials` lives in `RustPlusApi.Fcm` (shared) or moves to
`.Registration`; the listener needs it, so keep the type in `.Fcm` and have `.Registration`
*produce* it.

### 15.3 The two genuinely hard ports — point implementers at the source

These are not "write from the proto"; they are algorithm ports. Name the reference files
so nobody re-derives them:

- **Camera `rayData` decode (§5a).** The RLE depth/sample-buffer parse + projection is the
  riskiest code in the project. Port from rustplus.js
  [`Camera.js`](https://github.com/liamcottle/rustplus.js) and olijeffers0n
  `rustplus/remote/camera/camera_manager.py` (frame/ray parsing). Build it test-first
  against a **captured `AppCameraRays` fixture** in the mock — do not develop it live.
- **FCM/GCM registration (§7, steps 1–3).** Do **not** hand-roll from docs — mirror the
  *current* `@liamcottle/push-receiver` source function-for-function (checkin → FIS →
  register). The endpoints in the §7 table are the shape, but the exact request bodies,
  headers, and the Rust+ Firebase/Expo constants (sender id, project id, API key, app id,
  `experienceId`) must be **read from that source at implementation time**, not pinned from
  this document — they drift and any value written here would risk being stale. Centralize
  them in one `RustPlusConstants` file with a comment pointing at the upstream source.

### 15.4 One-time real-environment capture (you can't avoid it entirely)

The mock unlocks *offline* development, but its fixtures have to come from somewhere. Plan
for **one** real session, early, to record golden payloads: pair once against a live server
+ app, capture (a) a pairing FCM body, (b) a handful of `AppMessage` responses, (c) a short
`AppCameraRays` stream, as hex/base64 committed under `test/fixtures/`. After that, all
iteration is offline. (rustplus-desktop or any paired server works for the capture.)

### 15.5 Consumer migration guide

This is a breaking 2.0. Ship a `MIGRATION.md`: legacy→modern method mapping, the
`AppMessage` serializer change (protobuf-net types), the new TFMs, and the new packages.
Coordinate with the known consumer (rustplus-desktop, §0) — its Node-bundle removal is the
flagship migration story.

### 15.6 Known unknowns to resolve on first contact (not blockers, just flagged)

- Exact assembly/namespace holding `AppRequest` in the decompiled server (§6 Method A) —
  confirm on first decompile; may span `Assembly-CSharp.dll` / `Facepunch.*`.
- Whether `protobuf-net.BuildTools` generates cleanly for `netstandard2.0` *and* `net10`
  in one pass (§3) — if not, fall back to committed `protogen` output (already the fallback).
- Current FCM register endpoint/version (§7) — whichever `push-receiver` uses today wins.

---

### Sources

- [HandyS11/RustPlusApi](https://github.com/HandyS11/RustPlusApi)
- [liamcottle/rustplus.js](https://github.com/liamcottle/rustplus.js) — incl. [PairingFlow.md](https://github.com/liamcottle/rustplus.js/blob/master/docs/PairingFlow.md) and [rustplus.proto](https://github.com/liamcottle/rustplus.js/blob/master/rustplus.proto)
- [olijeffers0n/rustplus](https://github.com/olijeffers0n/rustplus) (Python; camera rendering reference + current `rustplus.proto`)
- [Pronwan/rustplus-desktop](https://github.com/Pronwan/rustplus-desktop) (C# WPF app; real downstream consumer of this library — validates §1/§5/§7/§9)
- [MatthieuLemoine/push-receiver](https://github.com/MatthieuLemoine/push-receiver) and the `@liamcottle/push-receiver` fork
- [fcm-push-listener (Rust)](https://crates.io/crates/fcm-push-listener), [crow-misia/go-push-receiver](https://github.com/crow-misia/go-push-receiver)
- [web-push-libs/web-push-csharp](https://github.com/web-push-libs/web-push-csharp) (send-side only — not an MCS receiver)
- [protobuf-net](https://github.com/protobuf-net/protobuf-net) and [contract-first docs](https://protobuf-net.github.io/protobuf-net/contract_first.html)
- [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp) (camera-frame rendering, netstandard2.0-compatible)
- Rust Dedicated Server (SteamCMD app `258550`) — authoritative protobuf-net contracts via decompilation (§6, Method A)
