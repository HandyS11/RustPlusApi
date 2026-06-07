# Proto-refresh tooling — implementation plan (§6, Method A)

> Decomposition of the cross-cutting task in [v2-progress.md](v2-progress.md#L184-L191).
> Source spec: §6 of [v2-plan.md](v2-plan.md#L422-L493).
> Branch: `refactor/v2`. This task is independent of the v2 phases and can land on its own PR.

## Goal

**Done when:** `tools/update-proto/` runs end-to-end and **reproduces the committed
[`RustPlusContracts.proto`](src/RustPlusApi/Protobuf/RustPlusContracts.proto) from a fresh
server download** — i.e. a clean run produces a `.proto` whose only diff against the
committed copy is intentional (new Facepunch fields), not tooling noise.

Two distinct outcomes ship from this task:
1. A **reproducible, scripted pipeline** committed under `tools/update-proto/`.
2. The **one-time v2 proto refresh** itself (the authoritative re-derivation the §0 diff
   called for: `AppInfo.camerasEnabled`, `AppCameraRays.timeOfDay`, real `Monument`/`Note`
   field names) — landed as its own reviewed diff against the committed proto.

## ⚠️ Finding (server build `23601104`) — the protobuf-net premise was wrong

§6 claims *"Facepunch uses protobuf-net internally"* and Step 3 calls
`Serializer.GetProto<AppMessage>()`. **Decompilation disproves this:**

- The Companion contracts live in **`Rust.Data.dll`**, namespace **`ProtoBuf`**, as
  **SilentOrbit-generated** `IProto<T>` classes — plain fields + hand-written
  `Serialize`/`Deserialize` methods. **No `[ProtoContract]`/`[ProtoMember]` attributes,
  no protobuf-net assembly** ships with the server.
- So `GetProto<T>()` is **not applicable**. Field numbers/types/names are instead recovered
  by **parsing the decompiled C#**: field number = `case <wirekey>: → wirekey >> 3`;
  type = declared C# field type + the `ProtocolParser.Read*` call; `required`/`optional`
  inferred from the `Serialize` method's guards.
- **Method A remains valid and authoritative** (the server is still the source of truth,
  names included) — only the Step-3 extraction mechanism changes. Verified end-to-end:
  the decoded `AppMap` matches the committed proto field-for-field.

## Key facts (verified in-repo)

- The committed schema is `proto2`, package `RustPlusContracts`, with **snake_case field
  names** — a deliberate Phase-2 decision (§14) so `protogen`/BuildTools emit uniform
  PascalCase C#. **The generator must reproduce snake_case**, or every field will appear
  changed.
- The proto is consumed at build time by `protobuf-net.BuildTools` as an `<AdditionalFiles>`
  entry in [RustPlusApi.csproj](src/RustPlusApi/RustPlusApi.csproj#L29) — there is no
  committed generated `.cs`. So the **`.proto` text is the artifact this tooling owns**.
- Rust dedicated server = SteamCMD app **`258550`**, anonymous login, no license needed.
- Facepunch compiles the Companion contracts (`AppRequest`, `AppMessage`,
  `AppCameraInput`, …) as `[ProtoContract]` classes into the server assemblies — same
  format v2 already targets. This is the whole reason Method A is primary.

## Open questions — RESOLVED (build `23601104`)

1. **Which assembly + namespace holds `AppRequest`/`AppMessage`?**
   ✅ **`Rust.Data.dll`, namespace `ProtoBuf`.** (`Assembly-CSharp.dll` only *references*
   them.) `2-decompile.sh` defaults to `Rust.Data.dll` with a `CONTRACT_DLL` override and a
   `--list` discovery mode.
2. **Unity/engine deps blocking standalone compilation?**
   ✅ **Moot** — the types aren't protobuf-net, so the *compile-and-reflect* approach (D1)
   is off the table entirely. Step 3 parses the decompiled C# text instead.
3. **Naming transform direction?**
   ✅ Decompiled fields are **camelCase** (`jpgImage`, `oceanMargin`); committed proto is
   **snake_case** (`jpg_image`, `ocean_margin`). Normalization = camelCase→snake_case.

## Proposed directory layout

```
tools/update-proto/
├── README.md                # what this is, prerequisites, how to run, cadence
├── update-proto.sh          # orchestrator: fetch → decompile → regenerate → diff
├── 1-fetch-server.sh        # SteamCMD wrapper (app 258550)
├── 2-decompile.sh           # ilspycmd decompile of Rust.Data.dll
├── ProtoGen/                # .NET tool: parses decompiled SilentOrbit C# → .proto
│   ├── ProtoGen.csproj      # (incl. snake_case + ordering normalization)
│   ├── Program.cs           # entry: parse server + committed, emit, write
│   ├── ServerParser.cs      # Roslyn: decompiled C# → message/enum model
│   ├── CommittedProto.cs    # committed proto → labels, order, preserved blocks
│   ├── Emitter.cs           # model → normalized .proto text
│   └── Model.cs             # Message / Field / EnumDef records
├── Directory.Build.props    # isolates the tool from the repo's shipping build settings
├── Directory.Packages.props # local (CPM off) so the tool pins its own deps
└── .gitignore               # ignore steamcmd/rds/decompiled/out + ProtoGen bin/obj
```

---

## Step-by-step decomposition

### Step 1 — SteamCMD fetch of the dedicated server (app `258550`)

- [ ] Write `1-fetch-server.sh`:
      `steamcmd +force_install_dir ./rds +login anonymous +app_update 258550 validate +quit`
- [ ] Make SteamCMD a documented prerequisite (not vendored): detect it on `PATH`, else
      print install guidance (package name per-OS / the Valve tarball). Allow a
      `STEAMCMD` env override mirroring the repo's `CHROME_PATH` convention.
- [ ] `.gitignore` the multi-GB `./rds` install dir.
- [ ] Capture the resolved **server build id / manifest** into the run output so a diff can
      be attributed to a specific Rust update.
- **Verify:** `rds/RustDedicated_Data/Managed/Assembly-CSharp.dll` exists after the run.

### Step 2 — Decompile the contract types with `ilspycmd`

- [ ] Document `dotnet tool install -g ilspycmd` as a prerequisite.
- [ ] Spike (resolves Open Question #1): list types in the managed assembly and locate the
      namespace containing `AppRequest`/`AppMessage`. Record the exact assembly + namespace
      in the README so the script isn't a black box.
- [ ] Write `2-decompile.sh` to decompile `Rust.Data.dll` into `./decompiled` (ilspycmd
      emits one `Rust.Data.decompiled.cs`; Step 3 scopes to the companion subset). ✅ done.
- [ ] `.gitignore` `./decompiled`. ✅ done.
- **Verify:** decompiled output contains `class AppMessage : … IProto<AppMessage>` (the
      SilentOrbit shape — **not** `[ProtoContract]`) for the known types. ✅ done.

### Step 3 — Regenerate `RustPlusContracts.proto` by parsing the decompiled SilentOrbit code

> `Serializer.GetProto<AppMessage>()` is **not applicable** (no protobuf-net — see the
> Finding section). `ProtoGen` is a parser over `decompiled/Rust.Data.decompiled.cs`.

**Mechanism (verified against `AppMap`):**

- **Scope.** Start from roots `AppRequest`, `AppMessage`, `AppBroadcast` and walk the
  **transitive closure** of referenced message/enum types within namespace `ProtoBuf`.
  Cross-check against the committed 45-message whitelist; anything new = a real addition to
  flag, anything missing = a removal to review.
- **Per message:** read field declarations (`public <ctype> <name>;`, skipping
  `ShouldPool`/`_disposed`/`[NonSerialized]`) for name + C# type; parse the
  `Deserialize(BufferStream, T instance, bool)` switch to map **field name → number**
  (`case <wirekey>: → number = wirekey >> 3`).
- **Type map** (declared C# type ⊕ `ProtocolParser.Read*`): `bool→bool`,
  `byte[]/ReadBytes→bytes`, `ReadSingle→float`, `ReadDouble→double`, `string→string`,
  `uint→uint32`, `int→int32`, `ulong→uint64`, `long→int64`, **`ReadZInt32→sint32`**,
  `List<T>→repeated T`, enum/message type name → itself.
- **Label:** `required` vs `optional` inferred from the `Serialize` method (required =
  written unconditionally; optional = guarded by a null/default check).
- **Enums:** parse C# `enum` bodies, honoring explicit values (`Switch = 1`) and implicit
  ordinals.

- [ ] Build `ProtoGen` (.NET 10 console tool) implementing the above; emit to `./out`.
- [ ] **Normalize** to match the committed convention (this is what makes the diff signal
      meaningful, not noise):
      - field names camelCase → **snake_case** (§14 convention);
      - `syntax = "proto2"` + `package RustPlusContracts` header;
      - stable message/field ordering;
      - **nesting policy** — committed proto nests `AppMap.Monument`; the server has a flat
        top-level `Monument`. Decide: re-nest to match committed, or flatten + accept the
        one-time diff. (Lean: re-nest, to keep the diff to real changes.)
      - (Resolved during the refresh: the committed `IconType`/`IconColor` proto enums were
        *redundant* with the library's own `NoteIcons`/`NoteColors` and unused in C#, so they were
        dropped in favour of the server's plain `int32` — no special-casing needed.)
- [ ] Wire `update-proto.sh` to run 1→2→3→normalize and write the candidate proto to
      `./out/RustPlusContracts.proto`.

**Diff & PR:**
- [ ] `update-proto.sh` ends with a `diff` of `./out/RustPlusContracts.proto` against the
      committed [src/RustPlusApi/Protobuf/RustPlusContracts.proto](src/RustPlusApi/Protobuf/RustPlusContracts.proto)
      and a non-zero exit when they differ (CI-friendly).
- [ ] **Reproducibility gate (the "Done when"):** on the *current* server build the diff
      must be **empty after normalization** — prove the pipeline reproduces today's proto.
- [ ] **One-time v2 refresh:** apply the real delta (expected `camerasEnabled`,
      `timeOfDay`, `Monument`/`Note` names), rebuild (BuildTools regenerates), run the test
      suite, and open the PR with the build id + diff in the description.
- **Verify:** library builds on both TFMs with the refreshed proto; existing
      `ProtobufRoundTripTests` stay green; any new fields surfaced are noted for follow-up
      mapper work (`Extensions/*`).

### Step 4 — Document the monthly rerun routine

- [x] `README.md`: prerequisites (SteamCMD, ilspycmd, .NET 10), one-command usage, and the
      **cadence**: Rust force-updates the **first Thursday of each month (~18:00–20:00 UTC)** —
      re-run after the update lands, review the diff, ship any delta.
- [x] Scheduled GitHub Action (`.github/workflows/ProtoRefresh.yml`) that **runs the pipeline
      and opens/updates a `proto-drift` issue** on first-Thursday (+ manual `workflow_dispatch`) —
      explicitly watching the authoritative server, **not** the community libs (drift-watch
      against olijeffers0n/liamcottle is rejected in §6). Never auto-applies the proto.

> Method D (live capture) and Method B (APK extraction) backups were **dropped** — Method A
> (decompile) has proven reliable end-to-end, so the backup runbooks were not worth maintaining.

---

## Sequencing & estimate

| Order | Step | Depends on | Nature | State |
| --- | --- | --- | --- | --- |
| 1 | Resolve Open Questions (spike) | — | investigation | ✅ done |
| 2 | Step 1 fetch script | spike | scripting | ✅ done (run against build `23601104`) |
| 3 | Step 2 decompile script | Step 1 | scripting | ✅ done |
| 4 | Step 3 ProtoGen (C# parser) + normalize | Step 2 | the hard part | ✅ done |
| 5 | Reproducibility gate | Step 3 | validation | ✅ done (now empty after refresh applied) |
| 6 | One-time v2 proto refresh | gate | applied | ✅ done (build clean, 51 tests green) |
| 7 | Step 4 docs + runbook (+opt. Action) | parallel w/ 2–4 | docs | ✅ done (Action still optional) |

### Reproducibility-gate result (build `23601104`)

The first run surfaced a **25-line, all-genuine diff** (the v2 refresh itself), since applied to
the committed proto:

- **New wire fields:** `AppCameraRays.time_of_day` (the §0 gap), `camera_position`/`camera_rotation`,
  `ClanInfo.score`, `ClanInfo.Role.can_access_score_events`, `AppMarker.SellOrder.price_multiplier`,
  `AppMarkerType.TravellingVendor`.
- **Renames/fixes:** `AppMap.Monument.name → token` (the §0 gap), `AppMarkerType.Unknow → Undefined`
  (committed typo).
- **Type widening:** 4× `entity_id`/`id` `uint32 → uint64` (server wraps in `NetworkableId`, read as `ReadUInt64`).
- **Note icon/colour adopted server truth:** `AppTeamInfo.Note` is now `int32 icon` /
  `int32 colour_index` / `string label` and the redundant `IconType`/`IconColor` proto enums were
  removed (the library already has its own `NoteIcons`/`NoteColors`; the mapper casts the ints).

With the refresh applied, **`update-proto.sh` now exits clean (empty diff)** against the current
server — so future monthly runs surface only *new* changes. The parser was validated by the
~440 lines that reproduced byte-for-byte on the first run.

## Risks & mitigations

- **SilentOrbit codegen shape varies across messages** (the parser's core assumption) →
  scope to the ~45-message companion subset (small, hand-verifiable); the reproducibility
  gate against the committed proto catches any field the parser gets wrong.
- **Decompiled camelCase ≠ committed snake_case** → the normalize step is mandatory, not
  optional; lock it with a golden-diff check against today's server.
- **Multi-GB SteamCMD download in CI** → keep fetch local/manual by default; the optional
  Action only *triggers/notifies*, it doesn't have to download in-pipeline.
- **Intentional library conventions diverging from the server** → review each diff and decide to
  adopt or keep; the gate stays informative. (The `IconType`/`IconColor` case turned out redundant
  and was dropped — see the gate-result note above.)
- **Obfuscation / decompiler breakage** → if it ever happens, fix the `CONTRACT_DLL`/parser or
  fall back to live-capture validation ad hoc; Method A has been reliable so no standing backup
  runbook is maintained.

## Acceptance checklist (maps to the §6 "Done when")

- [x] `tools/update-proto/update-proto.sh` runs fetch → decompile → generate → diff end-to-end.
- [x] On the current server build the output **matches the committed proto** modulo genuine
      server changes (25-line, all-explained diff) — reproducibility proven.
- [ ] One-time refreshed proto merged; library builds (both TFMs) and tests pass.
- [x] README documents prerequisites, usage, and the first-Thursday cadence.
