# Samples

Three console apps showing the libraries end to end. They fit together as one flow:

```
RustPlus.Register.ConsoleApp   ──▶  rustplus.config.json   ──▶  RustPlus.Fcm.ConsoleApp   (listen for pairing/alarms)
        (one-time setup)             + RustPlus(...) args   ──▶  RustPlus.ConsoleApp       (query/control the server)
```

See the [Samples article](https://handys11.github.io/RustPlusApi/articles/samples.html) on the docs site for a guided walkthrough.

> **Credentials are never committed.** The query and listener apps ship a placeholder template —
> `RustPlus.ConsoleApp/credentials.sample.json` and `RustPlus.Fcm.ConsoleApp/sample-config.json`.
> Copy it to the real file locally (`credentials.json` / `rustplus.config.json`), which is
> **gitignored**; don't put real values in the template. The Register app needs no template — it
> generates `rustplus.config.json` for you.

## 1. RustPlus.Register.ConsoleApp — get your credentials (start here)

The native, C#-only replacement for `npx @liamcottle/rustplus.js fcm-register`. It performs the
GCM/Firebase/FCM/Expo registration, opens **your default browser** for Steam login, registers with
Rust Companion, writes `rustplus.config.json`, then waits for you to pair in game.

```bash
dotnet run --project samples/RustPlus.Register.ConsoleApp
```

- **Any browser works.** If none opens (container, SSH), the sample prints the login URL — open it
  yourself and the flow continues.
- After Steam login, open Rust → join a server → **Pair with Server**.
- It prints the absolute path of the saved `rustplus.config.json` and the
  `new RustPlus(new RustPlusConnection(ip, port, playerId, playerToken))` line to use with the other samples.

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

Interactive menu covering the full `IRustPlus` surface:

- **Common** — info, map (saved to `map.jpg`), map markers, time, Nexus auth.
- **Team** — team info, team chat, promote to leader, send message.
- **Clan** — clan info, clan chat, send message, set MOTD.
- **Electricity** — alarms, subscriptions, storage monitors, smart switches (get/set/strobe/toggle).
- **Camera** — moved to the dedicated **RustPlus.Camera.ConsoleApp** sample (below).
- **Live Events** — stream smart-switch, storage-monitor, team/clan chat and clan-change events live.

Entity ids (alarm / smart switch / storage monitor) are remembered for the session — press
Enter at the prompt to reuse the last value.

```bash
# Copy credentials.sample.json to credentials.json and fill in the values
# printed by the Register app (ip / port / playerId / playerToken), then:
dotnet run --project samples/RustPlus.ConsoleApp
# or pass the path explicitly:
dotnet run --project samples/RustPlus.ConsoleApp -- /path/to/credentials.json
```

## 4. RustPlus.Camera.ConsoleApp — watch and control cameras

Dedicated camera sample built on the **RustPlusApi.Camera** package. Interactive mode opens a
managed `CameraController` session (keep-alive, device kind detected: static / PTZ / turret /
drone) with ASCII preview, PNG save, move & look, PTZ zoom, and turret shoot/reload
(press+release). Render fidelity is validated against real captured frames (golden tests,
2026-06-12). Uses the same `credentials.json` format as the query app.

```bash
dotnet run --project samples/RustPlus.Camera.ConsoleApp
# headless capture (render-fixture generation):
dotnet run --project samples/RustPlus.Camera.ConsoleApp -- [credentialsPath] capture <cameraId> <durationSeconds> [outputDir]
```

## Where the apps look for config

By default each app reads its config from its **build output directory**. The `.csproj` copies a
`credentials.json` / `rustplus.config.json` placed next to the project into the output on build,
so the simplest workflow is: put the file in the project folder and `dotnet run`. You can always
override the location by passing the path as the first argument.
