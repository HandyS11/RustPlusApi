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
        Console.WriteLine("Camera (rendering is experimental).");
        var cameraId = ids.GetString("cameraId");

        var response = await rustPlus.SubscribeToCameraAsync(cameraId);
        if (!response.IsSuccess)
        {
            DisplayUtilities.DisplayJson("SubscribeToCamera", response);
            return;
        }

        var info = response.Data!;
        var renderer = new CameraRenderer(info.Width, info.Height);

        void OnRays(object? _, CameraRaysEventArg frame) => renderer.AddRays(frame);
        rustPlus.OnCameraRaysReceived += OnRays;

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
                Console.WriteLine($"Supported controls: {(info.ControlFlags == CameraControlFlags.None ? "none (static camera)" : info.ControlFlags)}");
                Console.WriteLine("  p       : render an ASCII preview now");
                Console.WriteLine("  o       : save the current frame as a PNG");
                Console.WriteLine("  w/a/s/d : move (forward/left/back/right) — drones");
                Console.WriteLine("  i/j/k/l : look (up/left/down/right) — PTZ cameras / drones");
                Console.WriteLine("  space   : up (jump)");
                Console.WriteLine("  c       : down (duck)");
                Console.WriteLine("  x       : sprint");
                Console.WriteLine("  e       : use / interact");
                Console.WriteLine("  f/g/h   : fire primary/secondary/third — turrets");
                Console.WriteLine("  r       : reload — turrets");
                Console.WriteLine("  u       : unsubscribe and go back");
                Console.Write("\nPress a key: ");

                var key = char.ToLowerInvariant(Console.ReadKey(intercept: true).KeyChar);
                Console.WriteLine();

                switch (key)
                {
                    case 'p':
                        CameraAsciiRenderer.Print(renderer.Render());
                        Console.WriteLine("\nPress any key to continue...");
                        Console.ReadKey(intercept: true);
                        break;
                    case 'o':
                        var path = SavePng(cameraId, renderer.Render());
                        Console.WriteLine($"Saved {path}");
                        Console.WriteLine("\nPress any key to continue...");
                        Console.ReadKey(intercept: true);
                        break;
                    case 'w':
                        await MoveAsync(info, CameraButtons.Forward);
                        break;
                    case 'a':
                        await MoveAsync(info, CameraButtons.Left);
                        break;
                    case 's':
                        await MoveAsync(info, CameraButtons.Backward);
                        break;
                    case 'd':
                        await MoveAsync(info, CameraButtons.Right);
                        break;
                    case 'i':
                        await LookAsync(info, 0, -LookStep);
                        break;
                    case 'j':
                        await LookAsync(info, -LookStep, 0);
                        break;
                    case 'k':
                        await LookAsync(info, 0, LookStep);
                        break;
                    case 'l':
                        await LookAsync(info, LookStep, 0);
                        break;
                    case ' ':
                        await MoveAsync(info, CameraButtons.Jump);
                        break;
                    case 'c':
                        await MoveAsync(info, CameraButtons.Duck);
                        break;
                    case 'x':
                        await SendButtonAsync(info, CameraButtons.Sprint, CameraControlFlags.SprintAndDuck);
                        break;
                    case 'e':
                        await SendButtonAsync(info, CameraButtons.Use, requiredFlag: null);
                        break;
                    case 'f':
                        await SendButtonAsync(info, CameraButtons.FirePrimary, CameraControlFlags.Fire);
                        break;
                    case 'g':
                        await SendButtonAsync(info, CameraButtons.FireSecondary, CameraControlFlags.Fire);
                        break;
                    case 'h':
                        await SendButtonAsync(info, CameraButtons.FireThird, CameraControlFlags.Fire);
                        break;
                    case 'r':
                        await SendButtonAsync(info, CameraButtons.Reload, CameraControlFlags.Reload);
                        break;
                    case 'u':
                        running = false;
                        break;
                }
            }
        }
        finally
        {
            rustPlus.OnCameraRaysReceived -= OnRays;
            var unsub = await rustPlus.UnsubscribeFromCameraAsync();
            DisplayUtilities.DisplayJson("UnsubscribeFromCamera", unsub);
        }
    }

    private Task MoveAsync(CameraInfo info, CameraButtons buttons) =>
        SendButtonAsync(info, buttons, CameraControlFlags.Movement);

    private async Task SendButtonAsync(CameraInfo info, CameraButtons buttons, CameraControlFlags? requiredFlag)
    {
        if (requiredFlag is { } flag && !info.ControlFlags.HasFlag(flag))
        {
            Console.WriteLine(
                $"Note: this camera does not support {flag} (controls: {info.ControlFlags}); the server will ignore the input.");
        }

        var response = await rustPlus.SendCameraInputAsync(buttons);
        DisplayUtilities.DisplayJson($"SendCameraInput({buttons})", response);
    }

    private async Task LookAsync(CameraInfo info, float deltaX, float deltaY)
    {
        if (!info.ControlFlags.HasFlag(CameraControlFlags.Mouse))
        {
            Console.WriteLine(
                $"Note: this camera does not support mouse look (controls: {info.ControlFlags}); the server will ignore the input.");
        }

        var response = await rustPlus.SendCameraInputAsync(CameraButtons.None, deltaX, deltaY);
        DisplayUtilities.DisplayJson($"SendCameraInput(look {deltaX},{deltaY})", response);
    }

    private static string SavePng(string cameraId, byte[] pngBytes)
    {
        var safeId = string.Join("_", cameraId.Split(Path.GetInvalidFileNameChars()));
        var path = Path.GetFullPath($"camera_{safeId}_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        File.WriteAllBytes(path, pngBytes);
        return path;
    }
}
