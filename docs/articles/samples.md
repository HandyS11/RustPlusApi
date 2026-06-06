# Samples

The repository ships three runnable console apps under [`samples/`](https://github.com/HandyS11/RustPlusApi/tree/develop/samples)
that fit together as one flow:

```
RustPlus.Register.ConsoleApp  ──▶  rustplus.config.json  ──▶  RustPlus.Fcm.ConsoleApp  (listen)
       (one-time setup)            + RustPlus(...) args   ──▶  RustPlus.ConsoleApp      (control)
```

> Credentials are never committed. Each app ships a `*.sample.json` template; you create the real
> `credentials.json` / `rustplus.config.json` locally (both are gitignored).

## RustPlus.Register.ConsoleApp

The native replacement for `npx @liamcottle/rustplus.js fcm-register`. Performs registration,
opens Chrome/Chromium for Steam login, writes `rustplus.config.json`, then waits for you to pair
in game and prints the `RustPlus(...)` arguments.

```bash
dotnet run --project samples/RustPlus.Register.ConsoleApp
```

Requires Chrome or Chromium (native or Flatpak; `CHROME_PATH` overrides discovery).

## RustPlus.Fcm.ConsoleApp

Listens for pairing/alarm notifications using the credentials from the register app.

```bash
dotnet run --project samples/RustPlus.Fcm.ConsoleApp
# or: dotnet run --project samples/RustPlus.Fcm.ConsoleApp -- /path/to/rustplus.config.json
```

## RustPlus.ConsoleApp

Interactive menu for server info, map, team, smart switches, and more.

```bash
dotnet run --project samples/RustPlus.ConsoleApp
# or: dotnet run --project samples/RustPlus.ConsoleApp -- /path/to/credentials.json
```

By default each app reads its config from its build output directory; the `.csproj` copies a
`credentials.json` / `rustplus.config.json` placed next to the project into the output. You can
always pass the path as the first argument.
