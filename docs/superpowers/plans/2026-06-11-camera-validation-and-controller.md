# Camera Validation & Controller Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Live-server tasks (2 and 6) cannot run in a subagent sandbox without network access and must run from the main session.** They use the populated `samples/RustPlus.ConsoleApp/credentials.json` (gitignored — never commit it) and the in-game cameras `CAM02` (static CCTV, **outdoor** — use this one for the golden capture, the view is judgeable), `CAM01` (static CCTV, indoor — points inside a tiny building), and `TURRET01` (auto-turret, indoor).
>
> It may be night on the server during capture. That should not matter: render colours derive from material + surface alignment in the ray data, not in-game lighting (rustplus.js ignores `timeOfDay` too). Record the server time anyway (Task 2 Step 1) so the fixture is documented; if the outdoor image unexpectedly looks wrong, check the recorded time before suspecting the decode.

**Goal:** Close out everything marked *experimental/required* on the camera feature: validate render fidelity against a real captured frame (golden test), add the subscription keep-alive + turret/PTZ helpers the rustplus.js reference has and our port lacks, then remove the "experimental" warnings.

**Architecture:** Three thin layers stay as they are — protocol (`RustPlusApi`), rendering (`RustPlusApi.Camera`), sample (`RustPlus.ConsoleApp`). We add: a headless `capture` mode to the sample (produces JSON fixtures + PNG from a live camera), a golden characterization test in `RustPlusApi.Camera.UnitTests` fed by a committed fixture, and a new `CameraController` class in core `RustPlusApi` that wraps subscribe/resubscribe/input-helpers (faithful port of rustplus.js `Camera` class behaviour: 10 s re-subscribe, press+release zoom/shoot/reload, `IsAutoTurret` via the `Crosshair` flag).

**Tech Stack:** .NET (`netstandard2.0; net10.0` for libraries, `net10.0` for sample/tests), xUnit, `MockRustPlusServer` (in-repo), SixLabors.ImageSharp 2.1.x, `System.Text.Json` (sample + tests only — core stays JSON-free).

**ImageSharp decision (user-confirmed 2026-06-11):** stay on the 2.1.x line. It is the last Apache-2.0, netstandard2.0-compatible line (3.x needs net6+ and uses the Six Labors Split License, which would burden NuGet consumers). The repo already pins the latest patch (`2.1.13` in `Directory.Packages.props`) — no change needed; just don't downgrade.

**Reference:** liamcottle/rustplus.js `camera.js` (master, fetched 2026-06-11). Verified facts that shaped this plan:

- rustplus.js re-sends `cameraSubscribe` every **10 seconds** while subscribed (`subscribeInterval`); without refresh the server stops streaming rays.
- `zoom()` / `shoot()` are `FIRE_PRIMARY` press **then** `NONE` release; `reload()` is `RELOAD` then `NONE`. Two separate `cameraInput` requests.
- `isAutoTurret()` = `controlFlags & CROSSHAIR`.
- rustplus.js does **not** render entity overlays — its renderer is rays-only. (An earlier project note claiming otherwise was wrong; corrected.) Entity overlay is therefore **out of scope** (see bottom).

**Branch:** create `feature/camera-validation-controller` off `develop` before Task 1.

```bash
git -C /home/handys11/Dev/RustPlusApi checkout -b feature/camera-validation-controller develop
```

---

## File Structure

| File | Action | Responsibility |
| --- | --- | --- |
| `samples/RustPlus.ConsoleApp/Features/CameraCapture.cs` | Create | Headless capture: subscribe, record frames for N seconds, write fixture JSON + rendered PNG |
| `samples/RustPlus.ConsoleApp/Program.cs` | Modify | Recognise `capture <cameraId> <seconds> [outDir]` args and run headless instead of the menu |
| `tests/RustPlusApi.Camera.UnitTests/Fixtures/cam02-frames.json` | Create (Task 2 output) | Committed real-frame fixture from CAM02 (outdoor) |
| `tests/RustPlusApi.Camera.UnitTests/Fixtures/cam02-golden.png` | Create (Task 2 output) | Approved golden render of that fixture |
| `tests/RustPlusApi.Camera.UnitTests/CameraRendererGoldenTests.cs` | Create | Golden characterization test (fixture → pixel-exact match) |
| `tests/RustPlusApi.Camera.UnitTests/RustPlusApi.Camera.UnitTests.csproj` | Modify | Copy `Fixtures/**` to output |
| `src/RustPlusApi/CameraController.cs` | Create | Managed camera session: auto-resubscribe, frame event, zoom/shoot/reload helpers, `IsAutoTurret`, `IAsyncDisposable` |
| `tests/RustPlusApi.IntegrationTests/CameraControllerTests.cs` | Create | Controller behaviour against `MockRustPlusServer` |
| `samples/RustPlus.ConsoleApp/Features/CameraSession.cs` | Modify | Use `CameraController`; add zoom key; press+release fire/reload |
| `src/RustPlusApi.Camera/CameraRenderer.cs` | Modify (Task 7) | Drop "not yet validated" remark |
| `src/RustPlusApi.Camera/README.md`, `docs/articles/cameras.md`, `samples/README.md` | Modify (Task 7) | Replace experimental warnings with validated status + document `CameraController` and capture mode |

Build/test commands used throughout (run from repo root `/home/handys11/Dev/RustPlusApi`):

```bash
dotnet build RustPlusApi.sln
dotnet test tests/RustPlusApi.Camera.UnitTests
dotnet test tests/RustPlusApi.IntegrationTests
```

---

### Task 1: Headless frame-capture mode in the console sample

The golden validation needs raw frames from a live camera. The interactive `CameraSession` can't be driven by an agent, so add a non-interactive `capture` mode: subscribe, record every `CameraRaysEventArg` for a fixed duration (re-subscribing every 5 s so the stream doesn't die — `CameraController` doesn't exist yet), then write a JSON fixture and the rendered PNG.

**Files:**

- Create: `samples/RustPlus.ConsoleApp/Features/CameraCapture.cs`
- Modify: `samples/RustPlus.ConsoleApp/Program.cs`

The sample project has no test project (it's tooling); verification is build + the live run in Task 2.

- [ ] **Step 1: Write `CameraCapture.cs`**

```csharp
using System.Text.Json;
using RustPlusApi.Camera;
using RustPlusApi.Data.Events;
using RustPlusApi.Interfaces;

namespace RustPlus.ConsoleApp.Features;

/// <summary>
/// Headless capture for golden-fixture generation: subscribes to a camera, records every
/// received frame for a fixed duration (re-subscribing every 5 s to keep the stream alive),
/// then writes a JSON fixture of the raw frames plus the rendered PNG.
/// </summary>
internal sealed class CameraCapture(IRustPlus rustPlus)
{
    private static readonly TimeSpan ResubscribeInterval = TimeSpan.FromSeconds(5);

    internal sealed record CapturedFrame(int SampleOffset, string RayDataBase64);

    internal sealed record CaptureFixture(string CameraId, int Width, int Height, List<CapturedFrame> Frames);

    /// <returns>0 on success, 1 when the subscription fails, 2 when no frames arrived.</returns>
    public async Task<int> RunAsync(string cameraId, TimeSpan duration, string outputDirectory)
    {
        var response = await rustPlus.SubscribeToCameraAsync(cameraId);
        if (!response.IsSuccess)
        {
            Console.WriteLine($"SubscribeToCamera('{cameraId}') failed: {response.Error?.Code} {response.Error?.Message}");
            return 1;
        }

        var info = response.Data!;
        var renderer = new CameraRenderer(info.Width, info.Height);
        var fixture = new CaptureFixture(cameraId, info.Width, info.Height, []);

        void OnRays(object? _, CameraRaysEventArg frame)
        {
            lock (fixture.Frames)
            {
                fixture.Frames.Add(new CapturedFrame(frame.SampleOffset, Convert.ToBase64String(frame.RayData)));
                renderer.AddRays(frame);
            }
        }

        rustPlus.OnCameraRaysReceived += OnRays;
        try
        {
            var started = DateTime.UtcNow;
            var deadline = started + duration;
            while (DateTime.UtcNow < deadline)
            {
                var slice = deadline - DateTime.UtcNow;
                await Task.Delay(slice < ResubscribeInterval ? slice : ResubscribeInterval);

                int count;
                lock (fixture.Frames)
                {
                    count = fixture.Frames.Count;
                }

                Console.WriteLine($"[{(int)(DateTime.UtcNow - started).TotalSeconds,3}s] frames so far: {count}");

                if (DateTime.UtcNow < deadline)
                {
                    await rustPlus.SubscribeToCameraAsync(cameraId);
                }
            }
        }
        finally
        {
            rustPlus.OnCameraRaysReceived -= OnRays;
            await rustPlus.UnsubscribeFromCameraAsync();
        }

        Directory.CreateDirectory(outputDirectory);
        var safeId = string.Join("_", cameraId.Split(Path.GetInvalidFileNameChars())).ToLowerInvariant();
        var jsonPath = Path.Combine(outputDirectory, $"{safeId}-frames.json");
        var pngPath = Path.Combine(outputDirectory, $"{safeId}.png");

        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(fixture, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
        await File.WriteAllBytesAsync(pngPath, renderer.Render());

        Console.WriteLine($"Captured {fixture.Frames.Count} frames at {info.Width}x{info.Height} ({info.ControlFlags}).");
        Console.WriteLine($"Fixture: {jsonPath}");
        Console.WriteLine($"Image:   {pngPath}");
        return fixture.Frames.Count > 0 ? 0 : 2;
    }
}
```

- [ ] **Step 2: Wire the `capture` arg into `Program.cs`**

In `samples/RustPlus.ConsoleApp/Program.cs`, the first arg is currently an optional credentials path. Keep that, and treat a `capture` token (any position) as switching to headless mode. Replace the top of the file:

```csharp
using RustPlus.ConsoleApp.Features;
using RustPlus.ConsoleApp.Utils;

// Fill credentials.json (copy credentials.sample.json) with the ip/port/playerId/playerToken
// printed by the RustPlus.Register.ConsoleApp sample when you "Pair with Server" in game.
// Put it next to this project (gitignored), or pass its path as the first argument.
//
// Headless capture mode (golden-fixture generation):
//   RustPlus.ConsoleApp [credentialsPath] capture <cameraId> <durationSeconds> [outputDir]
var captureIndex = Array.FindIndex(args, a => a.Equals("capture", StringComparison.OrdinalIgnoreCase));
var configFilePath = args.Length > 0 && captureIndex != 0
    ? args[0]
    : Path.Combine(AppContext.BaseDirectory, "credentials.json");
```

Then, right after the existing `await rustPlus.ConnectAsync();` `try`/`catch` block and **before** `var ids = new EntityIdStore();`, insert:

```csharp
if (captureIndex >= 0)
{
    if (args.Length <= captureIndex + 2 || !int.TryParse(args[captureIndex + 2], out var seconds) || seconds <= 0)
    {
        Console.WriteLine("Usage: RustPlus.ConsoleApp [credentialsPath] capture <cameraId> <durationSeconds> [outputDir]");
        await rustPlus.DisconnectAsync();
        return;
    }

    var outDir = args.Length > captureIndex + 3 ? args[captureIndex + 3] : Environment.CurrentDirectory;
    var exitCode = await new CameraCapture(rustPlus).RunAsync(args[captureIndex + 1], TimeSpan.FromSeconds(seconds), outDir);
    await rustPlus.DisconnectAsync();
    Environment.Exit(exitCode);
}
```

- [ ] **Step 3: Build**

Run: `dotnet build samples/RustPlus.ConsoleApp`
Expected: Build succeeded, 0 warnings introduced (warnings-as-errors settings apply — match existing style).

- [ ] **Step 4: Verify the usage error path without credentials**

Run: `dotnet run --project samples/RustPlus.ConsoleApp -- capture`
Expected: connects (credentials.json is populated) then prints the `Usage:` line and exits — OR, if run where credentials are missing, prints the config-file error. Either confirms arg parsing doesn't crash.

- [ ] **Step 5: Commit**

```bash
git add samples/RustPlus.ConsoleApp/Features/CameraCapture.cs samples/RustPlus.ConsoleApp/Program.cs
git commit -m "feat(sample): headless camera capture mode for golden-fixture generation"
```

---

### Task 2: Live capture & visual validation against CAM02 ⚠ MAIN SESSION ONLY

This is the validation the "experimental" warnings have been waiting for. Run the capture against the live server, look at the PNG, and judge fidelity. **Use CAM02** — it points outdoors, so the image is judgeable (CAM01/TURRET01 look inside a tiny building; a wall close-up proves little). **Do not run in a sandboxed subagent** — needs network + credentials.

**Files:**

- Create: `tests/RustPlusApi.Camera.UnitTests/Fixtures/cam02-frames.json` (copied from capture output)
- Create: `tests/RustPlusApi.Camera.UnitTests/Fixtures/cam02-golden.png` (copied from capture output)

- [ ] **Step 1: Record server time, then capture 20 seconds from CAM02**

First check whether it's day or night in game — the menu's `Get Time` does this, or query it headlessly via a scratch script if preferred; either way, note `Time` / `Sunrise` / `Sunset` for the capture record. Night should **not** affect the render (colours come from material + alignment, not lighting), but knowing the time turns "the image looks odd" into a diagnosable fact instead of a guess.

Then run: `dotnet run --project samples/RustPlus.ConsoleApp -- capture CAM02 20 /tmp/camera-capture`
Expected: per-5s frame counts increasing, then `Captured N frames…` with N ≥ 10, and `/tmp/camera-capture/cam02-frames.json` + `/tmp/camera-capture/cam02.png` written. If N stays 0, stop and debug the subscription (check the camera id casing — identifiers are case-sensitive) before continuing.

- [ ] **Step 2: Visually inspect the PNG**

Open `/tmp/camera-capture/cam02.png` (Read tool renders images). Judge against what an outdoor Rust CCTV frame should look like: sky-coloured region (208,230,252) where the sky is, darker structured terrain/buildings below, no salt-and-pepper noise, no obvious vertical/horizontal scrambling. (The sky sentinel colour is fixed in the decode — it appears even at night.)

- **Pass** → continue.
- **Fail** → STOP this plan and switch to `superpowers:systematic-debugging`; compare decode behaviour against `/tmp/rustplusjs-camera.js` (`_renderCameraFrame`) frame-by-frame. The remaining tasks assume fidelity is confirmed. (Per project memory: the sample-offset doubling fix of 2026-06-11 was the last known decode bug.)

Optionally also capture CAM01 (`capture CAM01 10 /tmp/camera-capture`) for a second viewpoint — an indoor close-up should render flat wall materials, which is itself a sanity check — but only the CAM02 capture becomes the committed fixture.

- [ ] **Step 3: Trim the fixture if oversized**

The fixture will be committed; keep it lean. If `cam02-frames.json` exceeds ~1 MB, re-run the capture with a shorter duration (e.g. `capture CAM02 8 /tmp/camera-capture`) — enough frames to fill most of the image (the per-5s counts tell you the rate), then re-inspect the PNG as in Step 2.

- [ ] **Step 4: Copy fixture + golden into the test project**

```bash
mkdir -p tests/RustPlusApi.Camera.UnitTests/Fixtures
cp /tmp/camera-capture/cam02-frames.json tests/RustPlusApi.Camera.UnitTests/Fixtures/cam02-frames.json
cp /tmp/camera-capture/cam02.png tests/RustPlusApi.Camera.UnitTests/Fixtures/cam02-golden.png
```

The captured PNG **is** the golden: it is exactly `CameraRenderer.Render()` over those frames, and Step 2 approved it.

- [ ] **Step 5: Commit**

```bash
git add tests/RustPlusApi.Camera.UnitTests/Fixtures/
git commit -m "test: real CAM02 frame fixture and approved golden render"
```

---

### Task 3: Golden characterization test

Lock the approved render so any future decode/colour change that alters real-frame output fails CI.

**Files:**

- Create: `tests/RustPlusApi.Camera.UnitTests/CameraRendererGoldenTests.cs`
- Modify: `tests/RustPlusApi.Camera.UnitTests/RustPlusApi.Camera.UnitTests.csproj`

- [ ] **Step 1: Make the fixtures copy to output**

Add to `RustPlusApi.Camera.UnitTests.csproj` (inside the root `<Project>`, alongside the existing `ItemGroup`s):

```xml
<ItemGroup>
    <None Include="Fixtures\**" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

- [ ] **Step 2: Write the failing test**

Create `CameraRendererGoldenTests.cs`:

```csharp
using System.Text.Json;
using RustPlusApi.Camera;
using RustPlusApi.Data.Cameras;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace RustPlusApi.Camera.UnitTests;

/// <summary>
/// Golden test against a real frame sequence captured from a live server (CAM02, outdoor
/// CCTV, 2026-06-11). The fixture is raw protocol data (sampleOffset + rayData per frame);
/// the golden PNG is the visually-approved render of exactly those frames. Any decode/colour
/// change that alters real-frame output fails here. Refresh both files with:
///   dotnet run --project samples/RustPlus.ConsoleApp -- capture CAM02 20 out/
/// </summary>
public class CameraRendererGoldenTests
{
    private sealed record CapturedFrame(int SampleOffset, string RayDataBase64);

    private sealed record CaptureFixture(string CameraId, int Width, int Height, List<CapturedFrame> Frames);

    [Fact]
    public void Render_RealCapturedFrames_MatchesApprovedGoldenImage()
    {
        var fixture = JsonSerializer.Deserialize<CaptureFixture>(
            File.ReadAllText(Path.Combine("Fixtures", "cam02-frames.json")))!;

        var renderer = new CameraRenderer(fixture.Width, fixture.Height);
        foreach (var frame in fixture.Frames)
        {
            renderer.AddRays(new CameraFrame
            {
                SampleOffset = frame.SampleOffset,
                RayData = Convert.FromBase64String(frame.RayDataBase64)
            });
        }

        using var rendered = Image.Load<Rgba32>(renderer.Render());
        using var golden = Image.Load<Rgba32>(Path.Combine("Fixtures", "cam02-golden.png"));

        Assert.Equal(golden.Width, rendered.Width);
        Assert.Equal(golden.Height, rendered.Height);

        for (var y = 0; y < golden.Height; y++)
        {
            for (var x = 0; x < golden.Width; x++)
            {
                if (golden[x, y] != rendered[x, y])
                {
                    Assert.Fail($"Pixel mismatch at ({x},{y}): golden={golden[x, y]} rendered={rendered[x, y]}");
                }
            }
        }
    }
}
```

Note: the fixture records use PascalCase property names and `JsonSerializer.Serialize` wrote PascalCase (default, no naming policy), so default deserialization matches — no options needed.

- [ ] **Step 3: Run the test**

Run: `dotnet test tests/RustPlusApi.Camera.UnitTests --filter CameraRendererGoldenTests -v minimal`
Expected: **PASS** on first run — this is a characterization test of behaviour approved in Task 2, not red-green TDD. If it FAILS, the fixture/golden copy went wrong (e.g. fixture and golden from different capture runs) — redo Task 2 Step 4 from one single capture run.

- [ ] **Step 4: Run the whole camera test project**

Run: `dotnet test tests/RustPlusApi.Camera.UnitTests -v minimal`
Expected: all tests pass (existing pinned tests + golden).

- [ ] **Step 5: Commit**

```bash
git add tests/RustPlusApi.Camera.UnitTests/CameraRendererGoldenTests.cs tests/RustPlusApi.Camera.UnitTests/RustPlusApi.Camera.UnitTests.csproj
git commit -m "test: golden render test pinning real-frame fidelity"
```

---

### Task 4: `CameraController` — keep-alive, frame event, turret/PTZ helpers

Port of rustplus.js's `Camera` class lifecycle onto our protocol layer. Lives in core `RustPlusApi` (no image dependency). One controller per connection (the server tracks a single camera subscription per session, matching `UnsubscribeFromCameraAsync` taking no id).

**Files:**

- Create: `src/RustPlusApi/CameraController.cs`
- Test: `tests/RustPlusApi.IntegrationTests/CameraControllerTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/RustPlusApi.IntegrationTests/CameraControllerTests.cs`:

```csharp
using RustPlusApi.Data.Cameras;
using RustPlusApi.Data.Events;
using RustPlusApi.MockServer;
using RustPlusContracts;
using Xunit;

namespace RustPlusApi.IntegrationTests;

/// <summary>
/// Behaviour tests for <see cref="CameraController"/> against the mock server:
/// subscribe lifecycle, periodic re-subscribe keep-alive, frame forwarding,
/// press+release input helpers, and unsubscribe-on-dispose.
/// </summary>
public class CameraControllerTests
{
    private const ulong PlayerId = 76561198000000000;
    private const int PlayerToken = 123456789;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Condition not met within timeout.");
            await Task.Delay(25);
        }
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsControllerWithInfo()
    {
        await using var server = new MockRustPlusServer();
        server.Start();
        await using var client =
            new RustPlus(new RustPlusConnection(MockRustPlusServer.Host, server.Port, PlayerId, PlayerToken));
        await client.ConnectAsync().WaitAsync(Timeout);

        var response = await CameraController.SubscribeAsync(client, "CAM01").WaitAsync(Timeout);
        await using var controller = response.Data!;

        Assert.True(response.IsSuccess);
        Assert.Equal("CAM01", controller.CameraId);
        Assert.Equal(640, controller.Info.Width);
    }

    [Fact]
    public async Task SubscribeAsync_ResubscribesPeriodically()
    {
        var subscribeCount = 0;
        await using var server = new MockRustPlusServer(request =>
        {
            if (request.CameraSubscribe is not null)
            {
                Interlocked.Increment(ref subscribeCount);
            }

            return MockResponses.Default(request);
        });
        server.Start();
        await using var client =
            new RustPlus(new RustPlusConnection(MockRustPlusServer.Host, server.Port, PlayerId, PlayerToken));
        await client.ConnectAsync().WaitAsync(Timeout);

        var response = await CameraController
            .SubscribeAsync(client, "CAM01", resubscribeInterval: TimeSpan.FromMilliseconds(100))
            .WaitAsync(Timeout);
        await using var controller = response.Data!;

        // 1 initial subscribe + at least 2 keep-alive renewals
        await WaitUntilAsync(() => Volatile.Read(ref subscribeCount) >= 3);
    }

    [Fact]
    public async Task OnFrameReceived_ForwardsBroadcastFrames()
    {
        await using var server = new MockRustPlusServer();
        server.Start();
        await using var client =
            new RustPlus(new RustPlusConnection(MockRustPlusServer.Host, server.Port, PlayerId, PlayerToken));
        await client.ConnectAsync().WaitAsync(Timeout);

        var response = await CameraController.SubscribeAsync(client, "CAM01").WaitAsync(Timeout);
        await using var controller = response.Data!;

        var received = new TaskCompletionSource<CameraRaysEventArg>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        controller.OnFrameReceived += (_, e) => received.TrySetResult(e);

        await server.BroadcastAsync(MockResponses.CameraRaysBroadcast());
        var frame = await received.Task.WaitAsync(Timeout);

        Assert.Equal(65f, frame.VerticalFov);
        Assert.NotEmpty(frame.RayData);
    }

    [Fact]
    public async Task ShootAsync_SendsPressThenRelease()
    {
        var inputs = new List<int>();
        await using var server = new MockRustPlusServer(request =>
        {
            if (request.CameraInput is not null)
            {
                lock (inputs)
                {
                    inputs.Add(request.CameraInput.Buttons);
                }
            }

            return MockResponses.Default(request);
        });
        server.Start();
        await using var client =
            new RustPlus(new RustPlusConnection(MockRustPlusServer.Host, server.Port, PlayerId, PlayerToken));
        await client.ConnectAsync().WaitAsync(Timeout);

        var response = await CameraController.SubscribeAsync(client, "TURRET01").WaitAsync(Timeout);
        await using var controller = response.Data!;

        var result = await controller.ShootAsync().WaitAsync(Timeout);

        Assert.True(result.IsSuccess);
        lock (inputs)
        {
            Assert.Equal([(int)CameraButtons.FirePrimary, (int)CameraButtons.None], inputs);
        }
    }

    [Fact]
    public async Task ReloadAsync_SendsReloadThenRelease()
    {
        var inputs = new List<int>();
        await using var server = new MockRustPlusServer(request =>
        {
            if (request.CameraInput is not null)
            {
                lock (inputs)
                {
                    inputs.Add(request.CameraInput.Buttons);
                }
            }

            return MockResponses.Default(request);
        });
        server.Start();
        await using var client =
            new RustPlus(new RustPlusConnection(MockRustPlusServer.Host, server.Port, PlayerId, PlayerToken));
        await client.ConnectAsync().WaitAsync(Timeout);

        var response = await CameraController.SubscribeAsync(client, "TURRET01").WaitAsync(Timeout);
        await using var controller = response.Data!;

        var result = await controller.ReloadAsync().WaitAsync(Timeout);

        Assert.True(result.IsSuccess);
        lock (inputs)
        {
            Assert.Equal([(int)CameraButtons.Reload, (int)CameraButtons.None], inputs);
        }
    }

    [Fact]
    public async Task DisposeAsync_SendsUnsubscribe_AndStopsResubscribing()
    {
        var subscribeCount = 0;
        var unsubscribeCount = 0;
        await using var server = new MockRustPlusServer(request =>
        {
            if (request.CameraSubscribe is not null)
            {
                Interlocked.Increment(ref subscribeCount);
            }

            if (request.CameraUnsubscribe is not null)
            {
                Interlocked.Increment(ref unsubscribeCount);
            }

            return MockResponses.Default(request);
        });
        server.Start();
        await using var client =
            new RustPlus(new RustPlusConnection(MockRustPlusServer.Host, server.Port, PlayerId, PlayerToken));
        await client.ConnectAsync().WaitAsync(Timeout);

        var response = await CameraController
            .SubscribeAsync(client, "CAM01", resubscribeInterval: TimeSpan.FromMilliseconds(100))
            .WaitAsync(Timeout);

        await response.Data!.DisposeAsync();
        await WaitUntilAsync(() => Volatile.Read(ref unsubscribeCount) >= 1);

        var countAfterDispose = Volatile.Read(ref subscribeCount);
        await Task.Delay(400);
        Assert.Equal(countAfterDispose, Volatile.Read(ref subscribeCount));
    }

    [Fact]
    public async Task SubscribeAsync_FailureResponse_ReturnsErrorWithoutController()
    {
        await using var server = new MockRustPlusServer(request =>
        {
            if (request.CameraSubscribe is not null)
            {
                return new AppMessage
                {
                    Response = new AppResponse
                    {
                        Seq = request.Seq,
                        Error = new AppError
                        {
                            Error = "not_found"
                        }
                    }
                };
            }

            return MockResponses.Default(request);
        });
        server.Start();
        await using var client =
            new RustPlus(new RustPlusConnection(MockRustPlusServer.Host, server.Port, PlayerId, PlayerToken));
        await client.ConnectAsync().WaitAsync(Timeout);

        var response = await CameraController.SubscribeAsync(client, "NOPE").WaitAsync(Timeout);

        Assert.False(response.IsSuccess);
        Assert.Null(response.Data);
    }
}
```

Adjust the failure-test response shape if `MockResponses.Default` exposes a ready-made error helper — check `tests/RustPlusApi.MockServer/MockResponses.cs` first and reuse it if one exists (the `AppMessage`/`AppResponse`/`AppError` field names above follow `RustPlusContracts`; verify `Seq` casing against existing usages in that file).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/RustPlusApi.IntegrationTests --filter CameraControllerTests -v minimal`
Expected: FAIL — compile error: `CameraController` does not exist.

- [ ] **Step 3: Implement `CameraController`**

Create `src/RustPlusApi/CameraController.cs`. Constraints: `netstandard2.0`-compatible (no `PeriodicTimer`), `ConfigureAwait(false)` like the rest of the library, XML docs on all public members (docfx site).

```csharp
using RustPlusApi.Data;
using RustPlusApi.Data.Cameras;
using RustPlusApi.Data.Events;
using RustPlusApi.Interfaces;

namespace RustPlusApi;

/// <summary>
/// Managed session for one subscribed camera: keeps the subscription alive by periodically
/// re-sending the subscribe request (the server stops streaming rays for stale subscriptions),
/// forwards ray frames via <see cref="OnFrameReceived"/>, and exposes the press-and-release
/// input helpers (<see cref="ZoomAsync"/>, <see cref="ShootAsync"/>, <see cref="ReloadAsync"/>)
/// used by PTZ cameras and auto-turrets. Mirrors the <c>Camera</c> class of
/// liamcottle/rustplus.js. Dispose to stop the keep-alive and unsubscribe.
/// </summary>
/// <remarks>The server tracks a single camera subscription per connection, so create at most
/// one live controller per <see cref="IRustPlus"/> client at a time.</remarks>
public sealed class CameraController : IAsyncDisposable
{
    /// <summary>The keep-alive renewal period used when none is supplied (mirrors rustplus.js).</summary>
    public static readonly TimeSpan DefaultResubscribeInterval = TimeSpan.FromSeconds(10);

    private readonly IRustPlus _rustPlus;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _keepAlive;
    private bool _disposed;

    /// <summary>The identifier this controller is subscribed to (e.g. <c>CAM01</c>).</summary>
    public string CameraId { get; }

    /// <summary>Camera description from the most recent successful subscribe.</summary>
    public CameraInfo Info { get; private set; }

    /// <summary>Whether the camera is an auto-turret (advertises the
    /// <see cref="CameraControlFlags.Crosshair"/> flag, as in rustplus.js <c>isAutoTurret</c>).</summary>
    public bool IsAutoTurret => Info.ControlFlags.HasFlag(CameraControlFlags.Crosshair);

    /// <summary>Occurs when a ray frame for the subscribed camera is received.</summary>
    public event EventHandler<CameraRaysEventArg>? OnFrameReceived;

    private CameraController(IRustPlus rustPlus, string cameraId, CameraInfo info, TimeSpan resubscribeInterval)
    {
        _rustPlus = rustPlus;
        CameraId = cameraId;
        Info = info;
        _rustPlus.OnCameraRaysReceived += ForwardFrame;
        _keepAlive = resubscribeInterval > TimeSpan.Zero
            ? KeepAliveAsync(resubscribeInterval)
            : Task.CompletedTask;
    }

    /// <summary>
    /// Subscribes to <paramref name="cameraId"/> and returns a controller that keeps the
    /// subscription alive until disposed.
    /// </summary>
    /// <param name="rustPlus">A connected client.</param>
    /// <param name="cameraId">The camera identifier configured in game (case-sensitive).</param>
    /// <param name="resubscribeInterval">How often to renew the subscription;
    /// <see cref="DefaultResubscribeInterval"/> when <see langword="null"/>,
    /// <see cref="TimeSpan.Zero"/> or negative to disable renewal.</param>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    public static async Task<Response<CameraController?>> SubscribeAsync(IRustPlus rustPlus,
        string cameraId,
        TimeSpan? resubscribeInterval = null,
        CancellationToken cancellationToken = default)
    {
        var response = await rustPlus.SubscribeToCameraAsync(cameraId, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccess || response.Data is null)
        {
            return new Response<CameraController?>
            {
                IsSuccess = false, Error = response.Error
            };
        }

        return new Response<CameraController?>
        {
            IsSuccess = true,
            Data = new CameraController(rustPlus, cameraId, response.Data,
                resubscribeInterval ?? DefaultResubscribeInterval)
        };
    }

    /// <summary>Sends raw input: held buttons plus mouse deltas. Pass-through to
    /// <see cref="IRustPlus.SendCameraInputAsync"/>.</summary>
    /// <param name="buttons">The pressed <see cref="CameraButtons"/> bitmask.</param>
    /// <param name="mouseDeltaX">The horizontal mouse delta.</param>
    /// <param name="mouseDeltaY">The vertical mouse delta.</param>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    public Task<Response> SendInputAsync(CameraButtons buttons,
        float mouseDeltaX = 0,
        float mouseDeltaY = 0,
        CancellationToken cancellationToken = default) =>
        _rustPlus.SendCameraInputAsync(buttons, mouseDeltaX, mouseDeltaY, cancellationToken);

    /// <summary>Presses then releases <paramref name="buttons"/> (two input requests), the
    /// gesture PTZ cameras and turrets expect for discrete actions.</summary>
    /// <param name="buttons">The buttons to press and release.</param>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    public async Task<Response> PressAsync(CameraButtons buttons, CancellationToken cancellationToken = default)
    {
        var press = await SendInputAsync(buttons, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!press.IsSuccess)
        {
            return press;
        }

        return await SendInputAsync(CameraButtons.None, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Zooms a PTZ camera by one of its four levels (wraps back to level 1 from max).</summary>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    public Task<Response> ZoomAsync(CancellationToken cancellationToken = default) =>
        PressAsync(CameraButtons.FirePrimary, cancellationToken);

    /// <summary>Fires an auto-turret once.</summary>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    public Task<Response> ShootAsync(CancellationToken cancellationToken = default) =>
        PressAsync(CameraButtons.FirePrimary, cancellationToken);

    /// <summary>Reloads an auto-turret.</summary>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    public Task<Response> ReloadAsync(CancellationToken cancellationToken = default) =>
        PressAsync(CameraButtons.Reload, cancellationToken);

    /// <summary>Stops the keep-alive loop and unsubscribes from the camera (when still connected).</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _rustPlus.OnCameraRaysReceived -= ForwardFrame;
#if NET8_0_OR_GREATER
        await _cts.CancelAsync().ConfigureAwait(false);
#else
        _cts.Cancel();
#endif
        try
        {
            await _keepAlive.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is the expected shutdown path.
        }

        if (_rustPlus.IsConnected)
        {
            try
            {
                await _rustPlus.UnsubscribeFromCameraAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Best-effort, mirroring rustplus.js: the server drops stale
                // subscriptions on its own; never throw from dispose.
            }
        }

        _cts.Dispose();
    }

    private void ForwardFrame(object? sender, CameraRaysEventArg frame) => OnFrameReceived?.Invoke(this, frame);

    private async Task KeepAliveAsync(TimeSpan interval)
    {
        while (!_cts.IsCancellationRequested)
        {
            await Task.Delay(interval, _cts.Token).ConfigureAwait(false);
            var response = await _rustPlus.SubscribeToCameraAsync(CameraId, _cts.Token).ConfigureAwait(false);
            if (response is { IsSuccess: true, Data: not null })
            {
                Info = response.Data;
            }
        }
    }
}
```

Implementation notes for the executor:

- `KeepAliveAsync` is called from the constructor; `Task.Delay` runs first, so the constructor returns immediately and the initial subscribe is not duplicated. `OperationCanceledException` from `Task.Delay`/`SubscribeToCameraAsync` surfaces when awaited in `DisposeAsync` and is swallowed there.
- If the `#if NET8_0_OR_GREATER` guard doesn't match existing project conventions, check how the repo handles `CancelAsync` elsewhere (grep `CancelAsync` in `src/`) and copy that pattern; plain `_cts.Cancel()` everywhere is acceptable if that's the house style.
- `object? sender` nullable annotations: match the file style of `RustPlus.cs` (the project compiles netstandard2.0 with nullable enabled and polyfills).
- Run `dotnet format` or follow ReSharper settings if the build flags style (the repo had a ReSharper-formatting pass in #63).

- [ ] **Step 4: Run the controller tests**

Run: `dotnet test tests/RustPlusApi.IntegrationTests --filter CameraControllerTests -v minimal`
Expected: all 7 PASS.

- [ ] **Step 5: Run the full test suite (both TFMs build, no regressions)**

Run: `dotnet build RustPlusApi.sln && dotnet test tests/RustPlusApi.IntegrationTests && dotnet test tests/RustPlusApi.UnitTests`
Expected: build succeeds for `netstandard2.0` + `net10.0`; all tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusApi/CameraController.cs tests/RustPlusApi.IntegrationTests/CameraControllerTests.cs
git commit -m "feat: CameraController with subscription keep-alive and turret/PTZ helpers"
```

---

### Task 5: Sample `CameraSession` uses `CameraController`

The interactive sample currently subscribes directly (stream dies after the server-side subscription lapses) and sends fire/reload as press-only. Switch it to the controller and add a zoom key.

**Files:**

- Modify: `samples/RustPlus.ConsoleApp/Features/CameraSession.cs`

- [ ] **Step 1: Rewrite `CameraSession.RunAsync` around the controller**

Replace the subscribe/dispose plumbing and the fire/reload cases in `samples/RustPlus.ConsoleApp/Features/CameraSession.cs`:

- Replace lines 20–31 (direct `SubscribeToCameraAsync` + `OnCameraRaysReceived` hookup):

```csharp
        var response = await CameraController.SubscribeAsync(rustPlus, cameraId);
        if (!response.IsSuccess)
        {
            DisplayUtilities.DisplayJson("SubscribeToCamera", response);
            return;
        }

        await using var controller = response.Data!;
        var info = controller.Info;
        var renderer = new CameraRenderer(info.Width, info.Height);

        controller.OnFrameReceived += OnRays;

        void OnRays(object? _, CameraRaysEventArg frame)
        {
            lock (renderer)
            {
                renderer.AddRays(frame);
            }
        }
```

  (Add `using RustPlusApi;` if not already imported. Wrap the two `renderer.Render()` call sites in the same `lock (renderer)` so render and AddRays don't interleave.)

- In the menu text, add a zoom line and mark the turret status:

```csharp
                Console.WriteLine($"Supported controls: {(info.ControlFlags == CameraControlFlags.None ? "none (static camera)" : info.ControlFlags)}{(controller.IsAutoTurret ? " — auto-turret" : "")}");
                Console.WriteLine("  z       : zoom (PTZ cameras)");
```

- Replace the `'f'`/`'g'`/`'h'`/`'r'` cases with press+release helpers, and add `'z'`:

```csharp
                    case 'z':
                        DisplayUtilities.DisplayJson("Zoom", await controller.ZoomAsync());
                        break;
                    case 'f':
                        DisplayUtilities.DisplayJson("Shoot", await controller.ShootAsync());
                        break;
                    case 'g':
                        DisplayUtilities.DisplayJson("FireSecondary", await controller.PressAsync(CameraButtons.FireSecondary));
                        break;
                    case 'h':
                        DisplayUtilities.DisplayJson("FireThird", await controller.PressAsync(CameraButtons.FireThird));
                        break;
                    case 'r':
                        DisplayUtilities.DisplayJson("Reload", await controller.ReloadAsync());
                        break;
```

- Route the remaining input helpers through the controller: in `SendButtonAsync` and `LookAsync`, replace `rustPlus.SendCameraInputAsync(...)` with `controller.SendInputAsync(...)` (pass the controller in as a parameter, or convert those two private methods to local functions inside `RunAsync` so they close over `controller` — local functions are the smaller diff).
- Replace the `finally` block (manual event detach + `UnsubscribeFromCameraAsync`) with just `controller.OnFrameReceived -= OnRays;` — `await using` handles the unsubscribe. Print a line so behaviour stays observable: `Console.WriteLine("Unsubscribed.");`
- Keep the "server silently ignores unsupported inputs" advisory notes — they're correct and were hard-won.

- [ ] **Step 2: Build**

Run: `dotnet build samples/RustPlus.ConsoleApp`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add samples/RustPlus.ConsoleApp/Features/CameraSession.cs
git commit -m "feat(sample): camera session on CameraController — keep-alive, zoom, press+release turret actions"
```

---

### Task 6: Live validation — CAM02 long-run + TURRET01 helpers ⚠ MAIN SESSION ONLY

Proves the keep-alive matters and the turret helpers work against a real server. **Needs network + credentials; do not sandbox.**

- [ ] **Step 1: Long capture from CAM02 (keep-alive proof)**

Run: `dotnet run --project samples/RustPlus.ConsoleApp -- capture CAM02 60 /tmp/camera-longrun`
Expected: the per-5s frame counts keep growing across the whole 60 s (roughly linear rate). If frames stop arriving partway despite the 5 s re-subscribes, record the timing — that's new protocol knowledge; investigate before continuing (does a second subscribe stop the broadcast? does the count plateau at a server-side cap?).

- [ ] **Step 2: Capture TURRET01 and inspect**

Run: `dotnet run --project samples/RustPlus.ConsoleApp -- capture TURRET01 20 /tmp/camera-turret`
Expected: subscription succeeds and the printed `ControlFlags` include `Mouse`, `Fire`, `Reload`, `Crosshair` (auto-turrets are PTZ-controllable). View `/tmp/camera-turret/turret01.png` — should be a recognisable scene from the turret's viewpoint.

- [ ] **Step 3: Exercise turret helpers via a scratch script**

Create `/tmp/turret-check/Program.cs` as a throwaway console project (do **not** add it to the repo or the solution):

```bash
mkdir -p /tmp/turret-check && cd /tmp/turret-check && dotnet new console -n TurretCheck -o . --force
dotnet add reference /home/handys11/Dev/RustPlusApi/src/RustPlusApi/RustPlusApi.csproj
```

`Program.cs`:

```csharp
using System.Text.Json;
using RustPlusApi;

var creds = JsonDocument.Parse(File.ReadAllText(
    "/home/handys11/Dev/RustPlusApi/samples/RustPlus.ConsoleApp/credentials.json")).RootElement;

await using var rustPlus = new RustPlus(new RustPlusConnection(
    creds.GetProperty("ip").GetString()!,
    creds.GetProperty("port").GetInt32(),
    creds.GetProperty("playerId").GetUInt64(),
    creds.GetProperty("playerToken").GetInt32()));
await rustPlus.ConnectAsync();

var response = await CameraController.SubscribeAsync(rustPlus, "TURRET01");
if (!response.IsSuccess)
{
    Console.WriteLine($"Subscribe failed: {response.Error?.Code} {response.Error?.Message}");
    return;
}

await using var turret = response.Data!;
Console.WriteLine($"IsAutoTurret: {turret.IsAutoTurret} (flags: {turret.Info.ControlFlags})");

var shoot = await turret.ShootAsync();
Console.WriteLine($"Shoot:  success={shoot.IsSuccess} {shoot.Error?.Message}");

var reload = await turret.ReloadAsync();
Console.WriteLine($"Reload: success={reload.IsSuccess} {reload.Error?.Message}");
```

Run: `dotnet run --project /tmp/turret-check`
Expected: `IsAutoTurret: True`, both actions `success=True`. (Live caveat from project memory: this server acks every request, so a `True` here proves transport, not in-game effect — the in-game effect was implicitly granted by the user providing TURRET01 for testing. Note any anomalies.)

- [ ] **Step 4: Record findings**

Append a `## Live validation results (2026-06-11)` section to this plan file: server time at capture (from Task 2 Step 1), CAM02 60 s frame counts, TURRET01 control flags, helper outcomes, any surprises. Then `git add docs/superpowers/plans/2026-06-11-camera-validation-and-controller.md && git commit -m "docs: record live camera validation results"`.

---

### Task 7: De-experimentalize the docs

Only after Tasks 2, 3 and 6 pass. Replace "not validated / experimental" with the validated status, and document the new surface.

**Files:**

- Modify: `src/RustPlusApi.Camera/CameraRenderer.cs:14-18` (remarks block)
- Modify: `src/RustPlusApi.Camera/README.md:36-38`
- Modify: `docs/articles/cameras.md` (warning at lines 10-13; protocol section)
- Modify: `samples/RustPlus.ConsoleApp/Features/CameraSession.cs:17` (banner text)

- [ ] **Step 1: Update `CameraRenderer` remarks**

Replace the `<remarks>` block (lines 14–18) with:

```csharp
/// <remarks>
/// The ray decode, sample shuffle and colouring are ported from liamcottle/rustplus.js and
/// validated against frames captured from a live server (see the golden render test in
/// <c>RustPlusApi.Camera.UnitTests</c>, fixture captured 2026-06-11).
/// </remarks>
```

- [ ] **Step 2: Update the package README**

Replace lines 36–38 of `src/RustPlusApi.Camera/README.md` with:

```markdown
> Validated against real captured frames: a golden render test pins the decode output to a
> frame sequence captured from a live server. Refresh the fixture any time with the sample's
> headless capture mode (`RustPlus.ConsoleApp … capture <cameraId> <seconds> [outDir]`).
```

- [ ] **Step 3: Update `docs/articles/cameras.md`**

- Replace the `[!WARNING]` block (lines 10–13) with:

```markdown
> [!NOTE]
> Render fidelity is validated against real captured frames — a golden test in
> `RustPlusApi.Camera.UnitTests` pins the decode output to a frame sequence captured from a
> live server (2026-06-11).
```

- After the "Protocol layer" code block section, add a `## CameraController` section documenting: why keep-alive is needed (server stops streaming stale subscriptions; rustplus.js renews every 10 s), the factory + options, the helpers, and `IsAutoTurret`:

```markdown
## CameraController

`SubscribeToCameraAsync` alone is not enough for long sessions: the server stops streaming
rays for subscriptions that are not renewed. `CameraController` (in the core `RustPlusApi`
package) wraps the full session — it re-subscribes every 10 seconds (configurable), forwards
frames, and exposes the press-and-release gestures PTZ cameras and auto-turrets expect:

```csharp
var response = await CameraController.SubscribeAsync(rustPlus, "TURRET01");
if (!response.IsSuccess) return;

await using var turret = response.Data!;
turret.OnFrameReceived += (_, frame) => renderer.AddRays(frame);

if (turret.IsAutoTurret)        // ControlFlags has Crosshair
{
    await turret.ShootAsync();  // FirePrimary press + release
    await turret.ReloadAsync(); // Reload press + release
}

await turret.ZoomAsync();       // PTZ zoom: 4 levels, wraps to 1 from max
```

Disposing the controller stops the keep-alive and unsubscribes. Create at most one live
controller per client — the server tracks a single camera subscription per connection.

```

- [ ] **Step 4: Update the sample banner**

In `CameraSession.cs` line 17, change `"Camera (rendering is experimental)."` to `"Camera."`.

- [ ] **Step 5: Build docs project references + full suite**

Run: `dotnet build RustPlusApi.sln && dotnet test tests/RustPlusApi.Camera.UnitTests && dotnet test tests/RustPlusApi.IntegrationTests`
Expected: clean build, all green.

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusApi.Camera/CameraRenderer.cs src/RustPlusApi.Camera/README.md docs/articles/cameras.md samples/RustPlus.ConsoleApp/Features/CameraSession.cs
git commit -m "docs: camera rendering validated — drop experimental warnings, document CameraController"
```

---

## Live validation results (2026-06-12, in progress)

- **Server time at capture:** 13.68 (day; sunrise 7.55, sunset 19.85, day length 97.5 min).
- **Task 2 (CAM02 golden):** PASS. 20 s capture: 200 frames at a steady ~10 frames/s, 160×90, `ControlFlags: None` (static CCTV). Render visually coherent: sky-sentinel band, horizon, structures, no scrambling. Committed fixture re-captured at 8 s (81 frames, 588 KB) — identical, fully painted scene.
- **Keep-alive evidence (partial):** during the 20 s capture with 5 s re-subscribes, frame counts grew linearly across all four renewal cycles (50/101/152/199) — renewals do not interrupt or duplicate the stream.
- **NEW protocol finding (semantics corrected by the user):** `cameraSubscribe` fails with raw error `no_player` when the server has **no player entity for the paired account** — the user's character was killed mid-session (`GetTeamInfoAsync` → `IsOnline: false`, `IsAlive: false`). Per the user, cameras are accessed while the player is *disconnected* (sleeper present); the kill removed the sleeper and broke camera access. Library now maps the code: `RustPlusErrorCode.NoPlayer = 13` (commit b02265c; semantics fixed in a follow-up commit).
- **Task 6 long-run + TURRET01:** MOVED to the follow-up plan `2026-06-12-camera-live-validation-and-drone.md` (blocked on the player respawning; expanded to cover drone testing and the IsAutoTurret/IsDrone flag verification). All other tasks of this plan are complete; final branch review passed ("Ready to merge", 2026-06-12).

## Out of scope (deliberately)

- **Entity overlay rendering** (`CameraFrame.Entities` → boxes/name labels on the image). Verified 2026-06-11: rustplus.js master does *not* render entities — there is no faithful reference to port, only speculative projection math. Revisit as its own brainstorm/spec once someone needs it; the protocol layer already delivers `Entities` fully mapped.
- **Multi-camera multiplexing** — the server allows one camera subscription per connection; a second controller would need a second `RustPlus` client. Documented instead of engineered around.
- **Frame-rate shaping / 10-frame warm-up** (rustplus.js waits for 10 frames before first render). Our accumulate-forever buffer makes this unnecessary; callers can render whenever they like.

## Completion

After Task 7: run the full suite once more, then use **superpowers:finishing-a-development-branch** (PR target `develop`, per repo convention). Suggested PR title: `feat: camera validation (golden test) + CameraController keep-alive and turret helpers`.
