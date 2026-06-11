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
/// <param name="rustPlus">The connected Rust+ client to capture from.</param>
internal sealed class CameraCapture(IRustPlus rustPlus)
{
    private static readonly TimeSpan ResubscribeInterval = TimeSpan.FromSeconds(5);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal sealed record CapturedFrame(int SampleOffset, string RayDataBase64);

    internal sealed record CaptureFixture(string CameraId, int Width, int Height, List<CapturedFrame> Frames);

    /// <summary>Subscribes to <paramref name="cameraId"/>, captures frames for <paramref name="duration"/>, and writes the fixture to <paramref name="outputDirectory"/>.</summary>
    /// <param name="cameraId">The identifier of the camera to subscribe to.</param>
    /// <param name="duration">How long to record before unsubscribing and writing output.</param>
    /// <param name="outputDirectory">Directory where the JSON fixture and PNG are written.</param>
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

        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(fixture, JsonOptions));
        await File.WriteAllBytesAsync(pngPath, renderer.Render());

        Console.WriteLine($"Captured {fixture.Frames.Count} frames at {info.Width}x{info.Height} ({info.ControlFlags}).");
        Console.WriteLine($"Fixture: {jsonPath}");
        Console.WriteLine($"Image:   {pngPath}");
        return fixture.Frames.Count > 0 ? 0 : 2;
    }
}
