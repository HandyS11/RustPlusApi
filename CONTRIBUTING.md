# Contributing to RustPlusApi

Thank you for your interest in contributing! Bug reports, fixes, newly surfaced protocol fields and
documentation improvements are all welcome.

Before you start on a larger change, please [open an issue](https://github.com/HandyS11/RustPlusApi/issues/new/choose)
so we can agree on the approach. Questions go to [Discussions](https://github.com/HandyS11/RustPlusApi/discussions),
and security problems go through [SECURITY.md](SECURITY.md), never a public issue.

Everyone taking part is expected to follow the [Code of Conduct](CODE_OF_CONDUCT.md).

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download).
- The **.NET 8 runtime**. Each test project also runs on a `net8.0` host, which is how the
  `netstandard2.0` build of the libraries gets tested. Without it, run `-f net10.0` locally and let CI
  cover the other half.
- Git.
- A Rust server and the Rust+ app, only if you want to check a change live. The test suite runs
  against an in-process mock server and needs neither.

## Setup

```sh
git clone https://github.com/HandyS11/RustPlusApi.git
cd RustPlusApi

dotnet tool restore         # ReSharper formatter, ReportGenerator, Stryker
dotnet build                # also points git at the committed hooks in .githooks/
dotnet test RustPlusApi.sln
```

The first `dotnet build` runs `git config core.hooksPath .githooks`, which installs the
[pre-push hook](#formatting). There is nothing else to set up.

## Repository layout

| Path | What's there |
|---|---|
| `src/` | The six packages: `RustPlusApi`, `.Camera`, `.Fcm`, `.Fcm.Registration` and the two `*.Extensions.DependencyInjection` |
| `tests/` | Unit and integration tests per package, and `RustPlusApi.MockServer`, the in-process Rust+ server the integration tests share |
| `samples/` | Console apps for the client, camera, FCM listener and credential registration |
| `docs/` | The [DocFX site](https://handys11.github.io/RustPlusApi/): guides in `articles/`, contributor docs in `development/` |
| `tools/` | `update-proto` (regenerates the protocol from the Rust server) and `coverage` (the local coverage gate) |

## Building and testing

```sh
dotnet build                     # warnings are errors, see below
dotnet test RustPlusApi.sln      # every test project, on both hosts (net8.0 and net10.0)

# One test class or method (still both hosts):
dotnet test RustPlusApi.sln --filter "FullyQualifiedName~ClassName.MethodName"

# One host only:
dotnet test RustPlusApi.sln -f net8.0    # the netstandard2.0 build
dotnet test RustPlusApi.sln -f net10.0   # the net10.0 build

# Coverage, with a per-class report of anything below 100% and the CI gate:
tools/coverage/report.sh
```

[`docs/development/testing.md`](docs/development/testing.md) explains the test projects, the coverage
exclusions and mutation testing in detail. Read it before you change tests.

## What the build and CI enforce

- **Warnings are errors.** The build runs with `TreatWarningsAsErrors` and the `latest-all` analyzer
  set, plus Roslynator, SonarAnalyzer, the VS threading analyzers and the IDE code-style rules. Fix
  what they report. If a suppression is truly needed, put the reason in a comment next to it.
- **Two targets.** Every library builds for `netstandard2.0` and `net10.0`, so it runs on .NET
  Framework 4.6.2 and later as well as modern .NET. Missing APIs on the old target are bridged in
  `Polyfills/` or with `#if NET10_0_OR_GREATER`. Each such fork needs a test that runs on both
  targets and asserts the same result.
- **Coverage.** New code is expected at 100% line and branch coverage, and CI gates the merged total
  at 95% line and 90% branch. Code that can only run against the real network (a TLS handshake, a
  live Steam login) can be marked `[ExcludeFromCodeCoverage]` with a justification comment, and must
  be added to the exclusion list in `testing.md`. Test the logic behind it through `internal` seams
  instead.
- **Mutation testing.** A weekly Stryker run checks that tests actually pin behavior. Run it for one
  package from its unit-test directory:

  ```sh
  cd tests/RustPlusApi.Fcm.UnitTests
  dotnet stryker --config-file stryker-config.json --project RustPlusApi.Fcm.csproj
  ```

  Stryker can't mutate the core `RustPlusApi` package, so pin core behavior with exact-assertion
  unit tests.
- **Package versions** live in `Directory.Packages.props`. A `.csproj` references a package without
  a version.
- **Don't bump versions.** Local builds are always `1.0.0`. CD takes the real version from the
  release tag.

## Formatting

Formatting and member order come from the ReSharper command-line formatter, configured by
`.editorconfig` and the `ReformatAndReorder` profile in `RustPlusApi.sln.DotSettings`:

```sh
dotnet jb cleanupcode RustPlusApi.sln --profile="ReformatAndReorder"
```

The pre-push hook runs it on the files you're pushing and **rejects the push** if it changes
anything, leaving the fixed files in your working tree. Commit them and push again. See
[`docs/development/formatting.md`](docs/development/formatting.md).

## Protocol changes

`src/RustPlusApi/Protobuf/RustPlusContracts.proto` is authoritative: it is regenerated by decompiling
the Rust dedicated server (see [`tools/update-proto`](tools/update-proto/README.md)), and the C#
contracts are generated from it at compile time. Don't edit generated code.

Each month the ProtoRefresh workflow compares the proto with the latest server and opens a
`proto-drift` issue when they differ. To surface a new field:

1. Copy the field from the drift report into `RustPlusContracts.proto`.
2. Add a nullable property to the public model in `src/RustPlusApi/Data/`. A field the server leaves
   out should map to `null`, not to `0` or `false`, so read it through the generated
   `ShouldSerialize<Field>()` method.
3. Map it in `src/RustPlusApi/Extensions/`, with tests for both a present and an absent value.
4. Add it to the matching article in `docs/articles/`.

## Pull requests

1. Create a branch from `develop`, in a fork if you don't have write access.
2. Make the change, with tests.
3. Make sure `dotnet build` and `dotnet test RustPlusApi.sln` pass, and that the formatter has
   nothing to change.
4. Open the pull request **against `develop`** and fill in the template.

Give the pull request a [Conventional Commits](https://www.conventionalcommits.org/) title, such as
`feat(camera): add LookAsync` or `fix: map clan timestamps as milliseconds`. A workflow labels the pull
request from the title, and the label decides where the change appears in the release notes:

| Title type | Label | Release-notes section |
|---|---|---|
| `feat`, `perf` | `enhancement` | Features |
| `fix` | `bug` | Fixes |
| `docs` | `documentation` | Documentation |
| `chore`, `refactor`, `test`, `build`, `ci`, `style` | none | Internal |

Add `!` after the type for a breaking change (`feat!: …`), and describe what consumers have to change
in the template's *Breaking changes* section. The release notes are written from it.

CI builds the solution, runs every test on both hosts and enforces the coverage gate. SonarQube
analyses `develop` after the merge.

## Reporting issues

- **Bugs and feature requests:** the [issue tracker](https://github.com/HandyS11/RustPlusApi/issues/new/choose),
  using its templates. Remove tokens, Steam ids and server addresses from anything you paste.
- **Questions and ideas:** [Discussions](https://github.com/HandyS11/RustPlusApi/discussions).
- **Security vulnerabilities:** privately, as described in [SECURITY.md](SECURITY.md).
