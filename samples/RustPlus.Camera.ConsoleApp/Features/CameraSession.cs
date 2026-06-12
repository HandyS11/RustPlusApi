using RustPlus.Camera.ConsoleApp.Utils;
using RustPlus.ConsoleApp.Utils;
using RustPlusApi.Camera;
using RustPlusApi.Data.Cameras;
using RustPlusApi.Data.Events;
using RustPlusApi.Interfaces;

namespace RustPlus.Camera.ConsoleApp.Features;

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
        var renderer = new CameraRenderer(controller.Info.Width, controller.Info.Height);
        var renderLock = new object();

        controller.OnFrameReceived += OnRays;

        void OnRays(object? _, CameraRaysEventArg frame)
        {
            lock (renderLock)
            {
                renderer.AddRays(frame);
            }
        }

        try
        {
            var running = true;
            while (running)
            {
                Console.Clear();
                var flags = controller.Info.ControlFlags;
                Console.WriteLine($"Camera '{cameraId}' ({controller.Info.Width}x{controller.Info.Height}) — subscribed.");
                var deviceKind = "";
                if (controller.IsAutoTurret)
                {
                    deviceKind = " — auto-turret";
                }
                else if (controller.IsDrone)
                {
                    deviceKind = " — drone";
                }
                else if (controller.IsPtzCamera)
                {
                    deviceKind = " — PTZ camera";
                }

                Console.WriteLine($"Supported controls: {(flags == CameraControlFlags.None ? "none (static camera)" : flags)}{deviceKind}");

                // The menu only lists actions this device advertises; the controller refuses
                // anything else client-side (NotSupported) without sending — the server would
                // ack unsupported inputs with success while silently ignoring them.
                Console.WriteLine("  p       : render an ASCII preview now");
                Console.WriteLine("  o       : save the current frame as a PNG");
                if (flags.HasFlag(CameraControlFlags.Movement))
                {
                    Console.WriteLine("  w/a/s/d : move (forward/left/back/right, held ~0.5 s)");
                }

                if (flags.HasFlag(CameraControlFlags.Mouse))
                {
                    Console.WriteLine("  i/j/k/l : look (up/left/down/right)");
                }

                if (flags.HasFlag(CameraControlFlags.SprintAndDuck))
                {
                    Console.WriteLine("  space   : ascend (held ~0.5 s)");
                    Console.WriteLine("  c       : descend (held ~0.5 s)");
                }

                if (controller.IsPtzCamera)
                {
                    Console.WriteLine("  z       : zoom (cycles the zoom levels)");
                }

                if (controller.IsAutoTurret)
                {
                    Console.WriteLine("  f/g/h   : fire primary/secondary/third (press+release)");
                    Console.WriteLine("  r       : reload (press+release)");
                }

                Console.WriteLine("  e       : use / interact");
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
                        DisplayUtilities.DisplayJson("Move(Forward)", await controller.MoveAsync(CameraButtons.Forward));
                        break;
                    case 'a':
                        DisplayUtilities.DisplayJson("Move(Left)", await controller.MoveAsync(CameraButtons.Left));
                        break;
                    case 's':
                        DisplayUtilities.DisplayJson("Move(Backward)", await controller.MoveAsync(CameraButtons.Backward));
                        break;
                    case 'd':
                        DisplayUtilities.DisplayJson("Move(Right)", await controller.MoveAsync(CameraButtons.Right));
                        break;
                    case 'i':
                        DisplayUtilities.DisplayJson("Look(up)", await controller.LookAsync(0, -LookStep));
                        break;
                    case 'j':
                        DisplayUtilities.DisplayJson("Look(left)", await controller.LookAsync(-LookStep, 0));
                        break;
                    case 'k':
                        DisplayUtilities.DisplayJson("Look(down)", await controller.LookAsync(0, LookStep));
                        break;
                    case 'l':
                        DisplayUtilities.DisplayJson("Look(right)", await controller.LookAsync(LookStep, 0));
                        break;
                    case ' ':
                        // Sprint is the drone's ascend control (the SprintAndDuck flag).
                        DisplayUtilities.DisplayJson("Move(ascend)", await controller.MoveAsync(CameraButtons.Sprint));
                        break;
                    case 'c':
                        DisplayUtilities.DisplayJson("Move(descend)", await controller.MoveAsync(CameraButtons.Duck));
                        break;
                    case 'e':
                        DisplayUtilities.DisplayJson("Use", await controller.PressAsync(CameraButtons.Use));
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
