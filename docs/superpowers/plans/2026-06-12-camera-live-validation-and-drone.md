# Camera Live Validation & Drone Verification Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (or run inline from the main session). **Every task in this plan is live-server work: run it from the MAIN SESSION with network access and `samples/RustPlus.ConsoleApp/credentials.json` (gitignored — never commit it). Do NOT sandbox or delegate these tasks to subagents.** Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Finish the live validation deferred from the 2026-06-11 camera plan (60 s keep-alive proof, TURRET01 helpers) and verify the new device-kind discriminators (`IsAutoTurret` via `Reload`, `IsDrone` via `SprintAndDuck`) against a real turret AND a real drone — including the user's hypothesis that drones may advertise `Crosshair`.

**Architecture:** No new code expected. Uses the tooling already on branch `feature/camera-validation-controller`: the sample's headless `capture` mode and a scratch console project referencing `RustPlusApi`. Code changes happen ONLY if live flags contradict the discriminators (Task 4 says exactly what to change).

**Tech Stack:** existing branch tooling; `/tmp/rp-scratch` throwaway console project (never committed).

**Branch:** continue on `feature/camera-validation-controller` (do not create a new branch).

## Prerequisites (user actions — confirm before starting)

1. The paired player has **reconnected, respawned** (a character/sleeper exists again — the previous one was killed, causing `no_player`), and **disconnected** (cameras are accessed while the player is offline with a sleeper in the world).
2. `CAM02` (outdoor CCTV) and `TURRET01` (auto-turret) still configured.
3. A **drone** is paired/identified — ask the user for its identifier before Task 3 (placeholder below: `DRONE01`).
4. Heads-up given: Task 2 fires the real turret once and reloads it.

Quick gate check (run first; if it fails with `no_player`, stop and ask the user):

```bash
dotnet run --project samples/RustPlus.Camera.ConsoleApp -- capture CAM02 5 /tmp/camera-gate
```

Expected: `Captured N frames…` with N > 0.

---

### Task 1: CAM02 60-second long-run (keep-alive proof) ⚠ MAIN SESSION

- [ ] **Step 1: Run the long capture**

Run: `dotnet run --project samples/RustPlus.Camera.ConsoleApp -- capture CAM02 60 /tmp/camera-longrun`
Expected: per-5s frame counts grow roughly linearly for the full 60 s (~10 frames/s based on the 2026-06-12 short runs, so ~600 total). Any `Warning: re-subscribe failed` lines are findings — record them.

- [ ] **Step 2: Interpret**

- Counts grow all 60 s → keep-alive renewals sustain the stream: PASS, record final count.
- Counts plateau between renewals or after ~N seconds → measure when; that timing is the true server subscription TTL. Record it; if the TTL is under 10 s, change `CameraController.DefaultResubscribeInterval` accordingly (file `src/RustPlusApi.Camera/CameraController.cs`, the `TimeSpan.FromSeconds(10)` constant) and update `docs/articles/cameras.md` ("every 10 seconds").

---

### Task 2: TURRET01 — flags, helpers, render ⚠ MAIN SESSION

- [ ] **Step 1: Capture and record flags**

Run: `dotnet run --project samples/RustPlus.Camera.ConsoleApp -- capture TURRET01 15 /tmp/camera-turret`
Expected: success; note the printed `ControlFlags` (the summary line prints them). Prediction to verify: includes `Reload` (and likely `Mouse`, `Fire`, `Crosshair`).

- [ ] **Step 2: Inspect the PNG**

View `/tmp/camera-turret/turret01.png` (Read tool renders it). Expect a recognisable indoor scene from the turret's viewpoint.

- [ ] **Step 3: Exercise the controller helpers via the scratch project**

Set up once (reuse if `/tmp/rp-scratch` already exists from the previous session):

```bash
mkdir -p /tmp/rp-scratch && cd /tmp/rp-scratch && dotnet new console -o . --force
dotnet add reference /home/handys11/Dev/RustPlusApi/src/RustPlusApi/RustPlusApi.csproj
dotnet add reference /home/handys11/Dev/RustPlusApi/src/RustPlusApi.Camera/RustPlusApi.Camera.csproj
```

`/tmp/rp-scratch/Program.cs` (takes the camera id as arg; safe on any device — only fires when the device is a turret):

```csharp
using System.Text.Json;
using RustPlusApi;
using RustPlusApi.Camera;

var cameraId = args.Length > 0 ? args[0] : "TURRET01";
var creds = JsonDocument.Parse(File.ReadAllText(
    "/home/handys11/Dev/RustPlusApi/samples/RustPlus.ConsoleApp/credentials.json")).RootElement;

await using var rustPlus = new RustPlus(new RustPlusConnection(
    creds.GetProperty("ip").GetString()!,
    creds.GetProperty("port").GetInt32(),
    creds.GetProperty("playerId").GetUInt64(),
    creds.GetProperty("playerToken").GetInt32()));
await rustPlus.ConnectAsync();

var response = await CameraController.SubscribeAsync(rustPlus, cameraId);
if (!response.IsSuccess)
{
    Console.WriteLine($"Subscribe failed: {response.Error?.Code} {response.Error?.Message}");
    return;
}

await using var device = response.Data!;
Console.WriteLine($"ControlFlags: {device.Info.ControlFlags}");
Console.WriteLine($"IsAutoTurret: {device.IsAutoTurret}  IsDrone: {device.IsDrone}");

if (device.IsAutoTurret)
{
    var shoot = await device.ShootAsync();
    Console.WriteLine($"Shoot:  success={shoot.IsSuccess} {shoot.Error?.Message}");
    var reload = await device.ReloadAsync();
    Console.WriteLine($"Reload: success={reload.IsSuccess} {reload.Error?.Message}");
}
```

Run: `dotnet run --project /tmp/rp-scratch -- TURRET01`
Expected: `IsAutoTurret: True  IsDrone: False`, shoot/reload `success=True`. (Caveat from project memory: this server acks every request, so success proves transport; in-game effect is bonus evidence if observable.)

---

### Task 3: Drone — flags, IsDrone, movement ⚠ MAIN SESSION

Ask the user for the drone identifier first (assumed `DRONE01` below).

- [ ] **Step 1: Flags + discriminators**

Run: `dotnet run --project /tmp/rp-scratch -- DRONE01`
Record the full `ControlFlags` line — this is the key data point. Verify:

- `SprintAndDuck` present → `IsDrone: True` (user is confident of this flag).
- **Does the drone advertise `Crosshair`?** (the user's hypothesis for why rustplus.js-style `IsAutoTurret` via Crosshair is unsafe). Record yes/no either way.
- `Reload` absent → `IsAutoTurret: False`.

- [ ] **Step 2: Capture + render**

Run: `dotnet run --project samples/RustPlus.Camera.ConsoleApp -- capture DRONE01 15 /tmp/camera-drone`
View the PNG — a coherent scene from the drone's position. (Drone cameras stream like any other; this also validates the renderer against a third device type/resolution.)

- [ ] **Step 3: Movement input check (optional, careful)**

Drones actually fly when sent movement. Only if the user okays it: in the interactive camera sample (`dotnet run --project samples/RustPlus.Camera.ConsoleApp`, id `DRONE01` — commit 307dccd moved the camera session out of the RustPlus.ConsoleApp menu into this dedicated sample), tap `w` then `p` twice a few seconds apart — the ASCII preview should visibly change as the drone moves. Land/stop inputs after. If not okayed, skip — the acked `SendInputAsync` path is already mock-verified.

---

### Task 4: Discriminator verdict & (only if contradicted) code adjustment

- [ ] **Step 1: Fill the flag matrix**

| Device | Observed ControlFlags | IsAutoTurret | IsDrone |
| --- | --- | --- | --- |
| CAM02 (static CCTV) | `None` (verified 2026-06-12) | false | false |
| TURRET01 | *(Task 2)* | *(expect true)* | *(expect false)* |
| DRONE01 | *(Task 3)* | *(expect false)* | *(expect true)* |

- [ ] **Step 2: If expectations hold** — no code change; proceed to Task 5.

- [ ] **Step 3: If contradicted** (e.g. drone advertises `Reload`, or turret lacks it): pick the flag combination that uniquely separates the observed sets and update, in one commit:
- `src/RustPlusApi.Camera/CameraController.cs` — `IsAutoTurret` / `IsDrone` expressions + XML docs;
- `tests/RustPlusApi.Camera.IntegrationTests/CameraControllerTests.cs` — the `DeviceKindFlags` InlineData rows, using the REAL observed flag values;
- `docs/articles/cameras.md` — the paragraph after the CameraController example explaining the discriminators.
Then rerun: `dotnet test tests/RustPlusApi.Camera.IntegrationTests --filter CameraControllerTests -v minimal` (green) and re-verify live via the scratch script.

---

### Task 5: Record results and finish the branch

- [ ] **Step 1:** Append `## Live validation results (final)` to THIS plan file: long-run counts, full flag matrix with raw values, drone Crosshair finding, helper outcomes, any TTL discovery. (Plan files are gitignored — no commit for this.)

- [ ] **Step 2:** Update the `DeviceKindFlags` test comments in `CameraControllerTests.cs` if the synthetic flag combinations differ from observed reality (keep tests aligned with real wire values; commit if changed).

- [ ] **Step 3:** Full suite: `dotnet build RustPlusApi.sln && dotnet test tests/RustPlusApi.UnitTests && dotnet test tests/RustPlusApi.IntegrationTests && dotnet test tests/RustPlusApi.Camera.UnitTests` — all green.

- [ ] **Step 4:** Use **superpowers:finishing-a-development-branch** — PR `feature/camera-validation-controller` → `develop`. Suggested title: `feat!: camera validation (golden test), CameraController keep-alive, turret/drone helpers`. The `!` marks the public-API addition + the IsAutoTurret semantics differing from rustplus.js.

## Live validation results (final, 2026-06-12 afternoon)

Server time 15.35–15.45 (day). Devices rebuilt by the user, all looking outside: CAM01 (static), CCTV01 (PTZ), TURRET01 (static turret with look-around), DRONE01. Note: CAM02 from the first session no longer exists; the committed golden fixture remains valid (it is recorded data).

- **Issue root-cause correction (from user):** yesterday's `no_player` was caused by the **camera being destroyed**, not by the player's character state. Enum/doc comments corrected (commit 7ba92b4).
- **CameraController moved** to `RustPlusApi.Camera` per user preference (commit 7ba92b4): the camera package now owns session management + rendering; core stays pure protocol.
- **Keep-alive long-run (CAM01, 60 s): PASS.** 596 frames, perfectly linear ~10 frames/s across all eleven 5 s renewal cycles; zero re-subscribe failures.
- **Flag matrix (all observed live):**

| Device | ControlFlags (raw) | IsAutoTurret | IsDrone |
| --- | --- | --- | --- |
| CAM01 static CCTV | `None` (0) | false | false |
| CCTV01 PTZ | `Mouse, Fire` (10) | false | false |
| TURRET01 | `Mouse, Fire, Reload, Crosshair` (58) | **true** | false |
| DRONE01 | `Movement, Mouse, SprintAndDuck` (7) | false | **true** |

- **Discriminator verdict: both hold.** `Reload` uniquely marks the turret; `SprintAndDuck` uniquely marks the drone. This drone showed no `Crosshair`, but the PTZ camera carries `Fire` (its zoom button) — under rustplus.js's Crosshair rule a turret is still detected, but `Reload` is the more robust choice. Test rows updated to the live values (commit 163e146).
- **Helpers live:** TURRET01 `ShootAsync`/`ReloadAsync` → success; CCTV01 `ZoomAsync` → success; DRONE01 single gentle mouse-look → success (no movement inputs sent, per user caution). All renders coherent outdoor scenes on all four devices (sky band, horizon, structures); drone view framed by its parking spot.
- All tasks of this plan complete except the optional drone movement check (skipped deliberately). Remaining: finish the branch (PR).

## Addendum — actuation findings (2026-06-12, later session, capability-gating work)

Live probes with the new gated `CameraController` helpers (player disconnected throughout):

- **Server validates nothing:** every camera input is acked `success {}` even for buttons the
  device does not advertise (FirePrimary/Forward/Reload on static CAM01 all "succeeded").
  Client-side gating (`RustPlusErrorCode.NotSupported`, nothing sent) is the only feedback.
- **Actuation matrix** (observed via frame `CameraPosition`/`CameraRotation`/`VerticalFov`):
  CCTV01 look pans for real (rot y 2.34→4.42→2.35 restored) and zoom cycles four FOV levels
  (16.25→65→43.33→26→16.25, wraps).
- **DRONE01 FLIES — correction of an earlier wrong conclusion.** The first probes failed
  because they used `Jump` for up (the drone never took off, so everything else was a no-op).
  The vertical controls are `Sprint` (ascend) and `Duck` (descend) — that's what the
  `SprintAndDuck` flag means. Movement only actuates under a CONTINUOUS input stream (20 Hz
  verified); a single press-and-release or held press is acked and ignored. Verified flight:
  Sprint 1 s climbed ~4.7 m, Forward/Backward each moved ~5 m, Duck landed it back on its spot.
  Drone mouse look actuates while airborne only (ignored when parked) and changes the heading
  Forward/Backward move along. `MoveAsync` was redesigned to stream frames for a hold duration
  (default 500 ms) because of this. NOTE: the final library-API acceptance flight landed the
  drone ~6 m from its parking spot on lower ground (mid-flight look changed the heading) —
  user repositions via the app.
- **TURRET01 inconclusive:** the user checked with the official app — the turret is
  deactivated on this server, so its ignored look/shoot/reload inputs prove nothing about
  turret actuation in general.

## Context carried over from the 2026-06-11 plan

- Golden fixture/test landed (CAM02, day, 160×90, commit fd4a505/9e8be83); renderer fidelity validated and de-experimentalized.
- `no_player` = server has no player entity for the paired account (character killed while away); cameras are accessed while the player is **disconnected** with a sleeper present. Mapped as `RustPlusErrorCode.NoPlayer = 13`.
- Keep-alive partial evidence: 20 s capture with 5 s renewals streamed ~10 frames/s uninterrupted across four renewal cycles.
- `IsAutoTurret` deliberately diverges from rustplus.js (Reload, not Crosshair) per user guidance; `IsDrone` added (SprintAndDuck). Mock-verified; live verification is THIS plan.
