using System.Text.Json;
using RustPlusApi.Camera;
using RustPlusApi.Data.Cameras;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace RustPlusApi.Camera.UnitTests;

/// <summary>
/// Golden test against a real frame sequence captured from a live server (CAM02, outdoor
/// CCTV, 2026-06-12). The fixture is raw protocol data (sampleOffset + rayData per frame);
/// the golden PNG is the visually-approved render of exactly those frames. Any decode/colour
/// change that alters real-frame output fails here. Refresh both files with:
///   dotnet run --project samples/RustPlus.ConsoleApp -- capture CAM02 8 out/
/// </summary>
public class CameraRendererGoldenTests
{
#pragma warning disable CA1812 // instantiated by JsonSerializer.Deserialize<T>
    private sealed record CapturedFrame(int SampleOffset, string RayDataBase64);

    private sealed record CaptureFixture(string CameraId, int Width, int Height, List<CapturedFrame> Frames);
#pragma warning restore CA1812

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
