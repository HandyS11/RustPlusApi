# Samples

Three console apps showing the libraries end to end. They fit together as one flow:

```
RustPlus.Register.ConsoleApp   ──▶  rustplus.config.json   ──▶  RustPlus.Fcm.ConsoleApp   (listen for pairing/alarms)
        (one-time setup)             + RustPlus(...) args   ──▶  RustPlus.ConsoleApp       (query/control the server)
```

> **Credentials are never committed.** The query and listener apps ship a placeholder template —
> `RustPlus.ConsoleApp/credentials.sample.json` and `RustPlus.Fcm.ConsoleApp/sample-config.json`.
> Copy it to the real file locally (`credentials.json` / `rustplus.config.json`), which is
> **gitignored**; don't put real values in the template. The Register app needs no template — it
> generates `rustplus.config.json` for you.

## 1. RustPlus.Register.ConsoleApp — get your credentials (start here)

The native, C#-only replacement for `npx @liamcottle/rustplus.js fcm-register`. It performs the
GCM/Firebase/FCM/Expo registration, opens **Chrome/Chromium** for Steam login, registers with
Rust Companion, writes `rustplus.config.json`, then waits for you to pair in game.

```bash
dotnet run --project samples/RustPlus.Register.ConsoleApp
```

- **Requires Chrome or Chromium** (native or Flatpak; set `CHROME_PATH` to override discovery).
  Firefox/Safari won't work — the Steam step drives Chrome via the DevTools protocol.
- After Steam login, open Rust → join a server → **Pair with Server**.
- It prints the absolute path of the saved `rustplus.config.json` and the
  `new RustPlus(ip, port, playerId, playerToken)` line to use with the other samples.

## 2. RustPlus.Fcm.ConsoleApp — listen for FCM notifications

Listens for pairing/alarm notifications using the credentials from step 1.

```bash
# Copy the config produced by the Register app next to this project, then:
dotnet run --project samples/RustPlus.Fcm.ConsoleApp
# or pass the path explicitly:
dotnet run --project samples/RustPlus.Fcm.ConsoleApp -- /path/to/rustplus.config.json
```

It reads `rustplus.config.json` (the native format from step 1) and also accepts the legacy
`rustplus.js` config format as a fallback.

## 3. RustPlus.ConsoleApp — query and control a server

Interactive menu for server info, map, team, smart switches, etc.

```bash
# Copy credentials.sample.json to credentials.json and fill in the values
# printed by the Register app (ip / port / playerId / playerToken), then:
dotnet run --project samples/RustPlus.ConsoleApp
# or pass the path explicitly:
dotnet run --project samples/RustPlus.ConsoleApp -- /path/to/credentials.json
```

## Where the apps look for config

By default each app reads its config from its **build output directory**. The `.csproj` copies a
`credentials.json` / `rustplus.config.json` placed next to the project into the output on build,
so the simplest workflow is: put the file in the project folder and `dotnet run`. You can always
override the location by passing the path as the first argument.
