# `tools/update-proto` — authoritative proto refresh

Re-derives [`RustPlusContracts.proto`](../../src/RustPlusApi/Protobuf/RustPlusContracts.proto)
**authoritatively** by decompiling the Rust dedicated server. There is no official Rust+
schema; the community copies lag the game, so this is the primary, reproducible source of
truth.

> **Note (build `23601104`):** the server does **not** use protobuf-net — the contracts are
> **SilentOrbit**-generated `IProto<T>` classes in `Rust.Data.dll` (namespace `ProtoBuf`),
> with no `[ProtoContract]` attributes. So Step 3 **parses the decompiled C#**, it does not
> call `Serializer.GetProto<T>()`.

## Status

| Step | Script | State |
| --- | --- | --- |
| 1 — fetch server | `1-fetch-server.sh` | ✅ run (build `23601104`) |
| 2 — decompile contracts | `2-decompile.sh` | ✅ run (`Rust.Data.dll`, `--list` discovery) |
| 3 — parse C# → regenerate proto | `ProtoGen/` | ✅ reproduces committed proto modulo genuine server changes |
| orchestrator + diff gate | `update-proto.sh` | ✅ |
| 4 — cadence docs | this README | ✅ |

## Prerequisites

- **SteamCMD** — auto-installed (no sudo) by `1-fetch-server.sh` from Valve's Linux tarball
  into `./steamcmd` if missing; override with `STEAMCMD=/path/to/steamcmd`. Pulls a
  multi-GB server install. On some distros the tarball needs 32-bit glibc
  (Fedora: `sudo dnf install glibc.i686`).
- **.NET 10 SDK** (already required by this repo).
- **ilspycmd** — auto-installed by `2-decompile.sh` if missing (`dotnet tool install -g ilspycmd`).

## Usage

One command runs the whole pipeline (fetch → decompile → regenerate → diff):

```bash
cd tools/update-proto
./update-proto.sh
```

Exit `0` = the committed proto matches the current server; exit `1` = changes found (written
to `out/proto.diff` and printed). **The diff is the deliverable**: review it, apply the
genuine wire changes to [`RustPlusContracts.proto`](../../src/RustPlusApi/Protobuf/RustPlusContracts.proto),
rebuild, and open a PR.

While iterating you can skip the slow stages:

```bash
SKIP_FETCH=1 SKIP_DECOMPILE=1 ./update-proto.sh   # reuse ./rds and ./decompiled
```

Individual stages, if you need them:

```bash
./1-fetch-server.sh                 # SteamCMD pull of app 258550
./2-decompile.sh --list             # confirm which assembly defines the contracts
CONTRACT_DLL=<path> ./2-decompile.sh # override the Rust.Data.dll default if relocated
./2-decompile.sh                    # decompile into ./decompiled
dotnet run --project ProtoGen -- \
  decompiled/Rust.Data.decompiled.cs \
  ../../src/RustPlusApi/Protobuf/RustPlusContracts.proto out/RustPlusContracts.proto
```

### How ProtoGen works

`ProtoGen` (Roslyn) parses the decompiled SilentOrbit classes and regenerates the proto:
field **number** from each `Deserialize` dispatch arm (`wirekey >> 3`, or the field number
directly when the arm tests `key.Field`), **type** from the field declaration + the
`ProtocolParser.Read*` call (incl. `ReadZInt32 → sint32`, `NetworkableId/ReadUInt64 → uint64`,
`ArraySegment<byte> → bytes`), and **nesting** from the C# nested-class structure. It is
authoritative for names/types/numbers/repeated and for new fields/messages/enums; it preserves
the committed proto's conventions that the binary does not carry — `required`/`optional` labels,
declaration order, and hand-maintained well-known types (`Vector2/3/4`, `Color`, `Ray`).

The decompiler renders that dispatch as either a `switch` or an `if`/`else if` chain, and the
choice is not ours: it varies with the decompiler version *and* with Facepunch's code generator.
Build `25083359` moved single-field messages from `switch` to `if`, which is why both shapes are
read. See **Field-loss guard** below for what happens when a future shape fits neither.

> **Review, don't blindly overwrite.** A diff is the *server's* current truth; decide per change
> whether to adopt it (e.g. a field rename) or keep a deliberate library convention. With the v2
> refresh applied, the gate is currently empty.

### Field-loss guard

ProtoGen recovers wire information by pattern-matching decompiled C#. When it meets a dispatch
shape it does not recognise, it has no way to tell "this message has no fields" from "I could not
read this message" — so it used to emit an empty message and exit cleanly, and the drift report
presented the result as a genuine server-side field removal.

That is not hypothetical: it produced [#120](https://github.com/HandyS11/RustPlusApi/issues/120),
where 13 committed fields (`AppSendMessage.message`, `AppCameraSubscribe.camera_id`,
`AppError.error`, …) appeared to have been deleted by a game update. They had not been.

So ProtoGen now **fails with exit code 3** if any field in the committed proto is missing from the
regenerated one, naming each loss. Messages outside the authoritative closure are exempt: they are
copied verbatim from the committed proto and cannot lose anything.

Facepunch can of course genuinely remove a field. Confirm that deliberately:

```bash
dotnet run --project ProtoGen -- <decompiled.cs> <committed.proto> <out.proto> --allow-field-removal
```

Prefer reading the decompiled `Deserialize` of one named message first. A whole message emptying
out, or many messages each losing exactly one field, is the signature of a parser gap — not of a
game update.

### Reproducibility

`2-decompile.sh` pins `ilspycmd` (`ILSPY_VERSION`) and installs it into a script-local `.tools/`
directory rather than `-g`. The decompiled *shape* is ProtoGen's input, so an unpinned or ambient
decompiler makes the whole pipeline non-reproducible. Bump the pin consciously and re-run to
confirm the proto is unchanged. The version is stamped into `decompiled/.ilspy-version`, reported
in the drift issue, and the decompiled source is uploaded as a CI artifact so a surprising diff can
be traced to the shape that produced it without repeating the multi-GB fetch.

Working dirs (`steamcmd/`, `rds/`, `decompiled/`, `out/`, `ProtoGen/bin|obj`) are gitignored.

## Cadence (ongoing)

Rust force-updates on the **first Thursday of each month (~18:00–20:00 UTC)** — the only
moment the contracts can change. After the update lands: re-run this pipeline, review the
diff, ship any delta. We watch the **authoritative server**, never the community libs.

This is automated by [`.github/workflows/ProtoRefresh.yml`](../../.github/workflows/ProtoRefresh.yml):
every Thursday 21:00 UTC (gated to the first of the month) it runs the full pipeline and, on
drift, opens/updates a `proto-drift` issue with the diff. It can also be run on demand via
**workflow_dispatch**. It never auto-applies the proto — new fields usually need matching
model/mapper changes.
