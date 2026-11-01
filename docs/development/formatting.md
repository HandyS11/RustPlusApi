# Code Formatting

The solution is formatted with the **ReSharper command-line formatter**
([`JetBrains.ReSharper.GlobalTools`](https://www.jetbrains.com/help/resharper/CleanupCode.html)),
pinned in the dotnet tool manifest (`.config/dotnet-tools.json`) so every contributor runs the
same version. Formatting rules come from the repository `.editorconfig`.

---

## Formatting manually

```sh
dotnet tool restore
dotnet jb cleanupcode RustPlusApi.sln --profile="Built-in: Reformat Code"
```

The *Built-in: Reformat Code* profile only reformats — it never applies code-style rewrites or
syntax changes.

---

## The pre-push hook

A committed [`pre-push` hook](https://github.com/HandyS11/RustPlusApi/blob/develop/.githooks/pre-push)
keeps unformatted code from leaving your machine:

1. It collects the `*.cs`, `*.csproj`, `*.props` and `*.targets` files touched by the commits
   being pushed (new branches are compared against `origin/develop`).
2. It runs the formatter scoped to just those files.
3. If the formatter changes anything, the push is **rejected** and the fixed files are left in
   your working tree — review them, amend or commit, then push again.

Pre-existing local edits are ignored: only files the formatter itself modifies fail the check.

The same hook also lints the credentials web app's browser script when the push touches
`apps/RustPlusApi.CredentialsWeb/wwwroot/*.js` or that app's lint setup (`eslint.config.mjs`,
`tsconfig.json`, `package.json`, `package-lock.json`). It runs `npm run lint` in the app directory
(installing the pinned toolchain from the lockfile on first use) and rejects the push on any
finding. The ESLint configuration there mirrors the JavaScript rules SonarQube applies to `develop`
— typescript-eslint's type-aware promise rules plus SonarSource's `eslint-plugin-sonarjs` — so a
code smell fails on your machine rather than on the SonarQube dashboard after the merge. This step
needs Node.js; CI (`CI.yml`, job `lint-web`) runs the same lint on every push and pull request.

### Setup

None. Building any project once (`dotnet build`) points git at the committed hooks via
`git config core.hooksPath .githooks` (a target in `Directory.Build.props`; skipped on CI).

### Bypassing

For emergencies only:

```sh
git push --no-verify
```
