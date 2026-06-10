using RustPlus.ConsoleApp.Utils;
using RustPlusApi.Camera;
using RustPlusApi.Data.Cameras;
using RustPlusApi.Data.Events;
using RustPlusApi.Interfaces;

namespace RustPlus.ConsoleApp.Features;

internal sealed class CameraSession(IRustPlus rustPlus, EntityIdStore ids)
{
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
                Console.WriteLine("  p       : render an ASCII preview now");
                Console.WriteLine("  w/a/s/d : move (forward/left/back/right)");
                Console.WriteLine("  space   : jump");
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
                    case 'w':
                        await SendAsync(CameraButtons.Forward);
                        break;
                    case 'a':
                        await SendAsync(CameraButtons.Left);
                        break;
                    case 's':
                        await SendAsync(CameraButtons.Backward);
                        break;
                    case 'd':
                        await SendAsync(CameraButtons.Right);
                        break;
                    case ' ':
                        await SendAsync(CameraButtons.Jump);
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

    private async Task SendAsync(CameraButtons buttons)
    {
        var response = await rustPlus.SendCameraInputAsync(buttons);
        DisplayUtilities.DisplayJson($"SendCameraInput({buttons})", response);
    }
}
