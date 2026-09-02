# RustPlusApi — Agent Instructions

Six NuGet packages for the Rust+ companion app API, multi-targeting **netstandard2.0 + net10.0**.
See [CLAUDE.md](CLAUDE.md) for the full architecture overview.

## Build & Test

```bash
dotnet build                           # strict: TreatWarningsAsErrors + Roslynator/Sonar/VSTHRD analyzers
dotnet test RustPlusApi.sln            # both TFMs (net8.0 host → netstandard2.0 build; net10.0 host → net10.0 build)
dotnet test RustPlusApi.sln -f net8.0  # netstandard2.0 build only
dotnet test RustPlusApi.sln -f net10.0 # net10.0 build only
dotnet test RustPlusApi.sln --filter "FullyQualifiedName~ClassName.MethodName"

tools/coverage/report.sh               # full suite + merged coverage report + CI gate (line 95 / branch 90)

dotnet tool restore
dotnet jb cleanupcode RustPlusApi.sln --profile="ReformatAndReorder"
```

## Critical Conventions

- **Never bump versions in project files.** `Version=1.0.0` is the local placeholder; CD injects real versions via `-p:Version=<tag>`.
- **Format before pushing.** The pre-push hook (`.githooks/pre-push`, wired by `dotnet build`) runs the ReSharper formatter and **rejects** the push on any diff. Run `dotnet jb cleanupcode` first.
- **100% line/branch coverage is expected** for all new code. Exclusions require `[ExcludeFromCodeCoverage]` *with a justification comment*, and the member must be added to the exclusion list in [docs/development/testing.md](docs/development/testing.md).
- **`#if NET10_0_OR_GREATER` forks must be pinned by tests** asserting identical results on both TFMs. See the `HtmlColorParser` pattern in [docs/development/testing.md](docs/development/testing.md).
- **Stryker cannot mutate `RustPlusApi.csproj`** (protobuf-net.BuildTools breaks Stryker's rollback compiler). Pin core behavior with exact-assertion unit tests instead.

## Package Architecture

| Package | Key files | Role |
|---|---|---|
| `RustPlusApi` | `RustPlusSocket.cs`, `RustPlus.cs` | WebSocket client; `RustPlusSocket` = base (connect/send/receive), `RustPlus` = typed API + events |
| `RustPlusApi.Camera` | `CameraController.cs`, `CameraRenderer.cs` | Subscribe/keep-alive/input over a `RustPlus` instance; ImageSharp rendering |
| `RustPlusApi.Fcm` | `RustPlusFcmSocket.cs`, `RustPlusFcm.cs` | MCS protocol over TLS to `mtalk.google.com:5228`; FCM notification decryption |
| `RustPlusApi.Fcm.Registration` | `Steps/`, `SteamLoginService.cs` | GCM check-in → Firebase/FCM/Expo registration; Steam login via browser redirect to a loopback callback |
| `*.Extensions.DependencyInjection` | `*ServiceCollectionExtensions.cs` | `AddRustPlus`/`AddRustPlusFcm` + factories |

Protobuf contracts for core are **generated at compile time** by `protobuf-net.BuildTools` from `src/RustPlusApi/Protobuf/RustPlusContracts.proto`. Don't edit generated files in `obj/`.

## Test Strategy

See [docs/development/testing.md](docs/development/testing.md) for the full guide.

- **Unit tests** live in `tests/RustPlusApi*.UnitTests/`; **integration tests** use the shared in-process WebSocket server in `tests/RustPlusApi.MockServer/`.
- Live-network paths (TLS connect, Steam login, MCS handshake) are excluded from coverage with per-member justifications. Test them via internal seams (`InternalsVisibleTo`).
- `tests/RustPlusApi.NetFrameworkSmoke/` is compile-only (net48); it cannot run on Linux.
- Mutation testing: run per-package from the matching unit-test directory with `dotnet stryker --config-file stryker-config.json --project <PackageName>.csproj`. The core package cannot be mutated (see above).

## Code Style

- C# `latest`, `Nullable enable`, `ImplicitUsings enable`
- 4-space indent, 120-column max, LF line endings (enforced by `.editorconfig`)
- All analyzer warnings are errors — fix them, don't suppress
