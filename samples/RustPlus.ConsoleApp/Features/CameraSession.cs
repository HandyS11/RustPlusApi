using RustPlus.ConsoleApp.Utils;
using RustPlusApi.Camera;
using RustPlusApi.Data.Cameras;
using RustPlusApi.Data.Events;
using RustPlusApi.Interfaces;

namespace RustPlus.ConsoleApp.Features;

internal sealed class CameraSession(IRustPlus rustPlus, EntityIdStore ids)
{
    /// <summary>How far one mouse-look keypress turns the camera, in mouse-delta units.</summary>
    private const float LookStep = 10f;

    public async Task RunAsync()
    {
        Console.Clear();
        Console.WriteLine("Camera.");
        var cameraId = ids.GetString("cameraId");

        var response = await CameraController.SubscribeAsync(rustPlus, cameraId);
        if (!response.IsSuccess)
        {
            DisplayUtilities.DisplayJson("SubscribeToCamera", response);
            return;
        }

        await using var controller = response.Data!;
        var info = controller.Info;
        var renderer = new CameraRenderer(info.Width, info.Height);
        var renderLock = new object();

        controller.OnFrameReceived += OnRays;

        void OnRays(object? _, CameraRaysEventArg frame)
        {
            lock (renderLock)
            {
                renderer.AddRays(frame);
            }
        }

        async Task MoveAsync(CameraButtons buttons) =>
            await SendButtonAsync(buttons, CameraControlFlags.Movement);

        async Task SendButtonAsync(CameraButtons buttons, CameraControlFlags? requiredFlag)
        {
            if (requiredFlag is { } flag && !info.ControlFlags.HasFlag(flag))
            {
                Console.WriteLine(
                    $"Note: this camera does not support {flag} (controls: {info.ControlFlags}); the server will ignore the input.");
            }

            var r = await controller.SendInputAsync(buttons);
            DisplayUtilities.DisplayJson($"SendCameraInput({buttons})", r);
        }

        async Task LookAsync(float deltaX, float deltaY)
        {
            if (!info.ControlFlags.HasFlag(CameraControlFlags.Mouse))
            {
                Console.WriteLine(
                    $"Note: this camera does not support mouse look (controls: {info.ControlFlags}); the server will ignore the input.");
            }

            var r = await controller.SendInputAsync(CameraButtons.None, deltaX, deltaY);
            DisplayUtilities.DisplayJson($"SendCameraInput(look {deltaX},{deltaY})", r);
        }

        try
        {
            var running = true;
            while (running)
            {
                Console.Clear();
                Console.WriteLine($"Camera '{cameraId}' ({info.Width}x{info.Height}) — subscribed.");
                // The server silently ignores inputs the camera does not advertise: WASD only moves
                // drones (Movement flag), mouse-look only turns PTZ-style cameras (Mouse flag) —
                // a static CCTV reports None and never reacts, even though the input is acked.
                var deviceKind = "";
                if (controller.IsAutoTurret)
                {
                    deviceKind = " — auto-turret";
                }
                else if (controller.IsDrone)
                {
                    deviceKind = " — drone";
                }

                Console.WriteLine($"Supported controls: {(info.ControlFlags == CameraControlFlags.None ? "none (static camera)" : info.ControlFlags)}{deviceKind}");
                Console.WriteLine("  p       : render an ASCII preview now");
                Console.WriteLine("  o       : save the current frame as a PNG");
                Console.WriteLine("  w/a/s/d : move (forward/left/back/right) — drones");
                Console.WriteLine("  i/j/k/l : look (up/left/down/right) — PTZ cameras / drones");
                Console.WriteLine("  space   : up (jump)");
                Console.WriteLine("  c       : down (duck)");
                Console.WriteLine("  x       : sprint");
                Console.WriteLine("  e       : use / interact");
                Console.WriteLine("  z       : zoom (PTZ cameras)");
                Console.WriteLine("  f/g/h   : fire primary/secondary/third — turrets (press+release)");
                Console.WriteLine("  r       : reload — turrets (press+release)");
                Console.WriteLine("  u       : unsubscribe and go back");
                Console.Write("\nPress a key: ");

                var key = char.ToLowerInvariant(Console.ReadKey(intercept: true).KeyChar);
                Console.WriteLine();

                switch (key)
                {
                    case 'p':
                        byte[] asciiFrame;
                        lock (renderLock)
                        {
                            asciiFrame = renderer.Render();
                        }
                        CameraAsciiRenderer.Print(asciiFrame);
                        Console.WriteLine("\nPress any key to continue...");
                        Console.ReadKey(intercept: true);
                        break;
                    case 'o':
                        byte[] pngFrame;
                        lock (renderLock)
                        {
                            pngFrame = renderer.Render();
                        }
                        var path = SavePng(cameraId, pngFrame);
                        Console.WriteLine($"Saved {path}");
                        Console.WriteLine("\nPress any key to continue...");
                        Console.ReadKey(intercept: true);
                        break;
                    case 'w':
                        await MoveAsync(CameraButtons.Forward);
                        break;
                    case 'a':
                        await MoveAsync(CameraButtons.Left);
                        break;
                    case 's':
                        await MoveAsync(CameraButtons.Backward);
                        break;
                    case 'd':
                        await MoveAsync(CameraButtons.Right);
                        break;
                    case 'i':
                        await LookAsync(0, -LookStep);
                        break;
                    case 'j':
                        await LookAsync(-LookStep, 0);
                        break;
                    case 'k':
                        await LookAsync(0, LookStep);
                        break;
                    case 'l':
                        await LookAsync(LookStep, 0);
                        break;
                    case ' ':
                        await MoveAsync(CameraButtons.Jump);
                        break;
                    case 'c':
                        await MoveAsync(CameraButtons.Duck);
                        break;
                    case 'x':
                        await SendButtonAsync(CameraButtons.Sprint, CameraControlFlags.SprintAndDuck);
                        break;
                    case 'e':
                        await SendButtonAsync(CameraButtons.Use, requiredFlag: null);
                        break;
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
                    case 'u':
                        running = false;
                        break;
                }
            }
        }
        finally
        {
            controller.OnFrameReceived -= OnRays;
        }

        Console.WriteLine("Unsubscribed.");
    }

    private static string SavePng(string cameraId, byte[] pngBytes)
    {
        var safeId = string.Join("_", cameraId.Split(Path.GetInvalidFileNameChars()));
        var path = Path.GetFullPath($"camera_{safeId}_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        File.WriteAllBytes(path, pngBytes);
        return path;
    }
}
