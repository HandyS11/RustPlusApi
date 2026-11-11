<!--
Title: use the Conventional Commits form, e.g. `feat(camera): add LookAsync` or `fix: …`.
The type sets the label, and the label sets the release-notes section:
feat/perf → Features, fix → Fixes, docs → Documentation, anything else → Internal.
Add `!` after the type for a breaking change (`feat!: …`). See CONTRIBUTING.md.
-->

## Summary

<!-- What does this change, and why? Link the issue it closes below. -->

## Breaking changes

<!-- Public API or behavior a consumer has to adapt to, with the old → new shape. Write "None" if none. -->

## How it was tested

<!-- Automated tests added or changed. Say if it was also checked against a live server, and which parts were not. -->

## Checklist

- [ ] `dotnet build` passes with no warnings
- [ ] `dotnet test RustPlusApi.sln` passes on both test hosts (net8.0 runs the netstandard2.0 build, net10.0 the net10.0 build)
- [ ] New code is fully covered (`tools/coverage/report.sh`), and any `[ExcludeFromCodeCoverage]` is justified in `docs/development/testing.md`
- [ ] Any `#if NET10_0_OR_GREATER` fork is pinned by a test that runs on both targets
- [ ] Formatted with `dotnet jb cleanupcode RustPlusApi.sln --profile="ReformatAndReorder"` (the pre-push hook checks this)
- [ ] Package READMEs and `docs/articles/` updated if the public API changed
- [ ] No version bumped in project files

## Related issues

<!-- Closes #123 -->
