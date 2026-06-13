using System.Text.Json;
using RustPlusApi.Camera;
using RustPlusApi.Data.Cameras;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace RustPlusApi.Camera.UnitTests;

/// <summary>
/// Golden tests against real frame sequences captured live (2026-06-12) from every camera
/// device type: static CCTV (cam01, cam02), PTZ camera (cctv01), auto-turret (turret01) and
/// drone (drone01). Each fixture is raw protocol data (sampleOffset + rayData per frame);
/// each golden PNG is the visually-approved render of exactly those frames. Any decode/colour
/// change that alters real-frame output fails here. Refresh a fixture with the sample's
/// headless capture mode:
///   dotnet run --project samples/RustPlus.Camera.ConsoleApp -- capture &lt;cameraId&gt; 8 out/
/// </summary>
public class CameraRendererGoldenTests
{
    /// <summary>Fixtures are copied next to the test assembly; resolve them from there rather
    /// than the working directory, which varies by test runner.</summary>
    private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    [Theory]
    [InlineData("cam01")] // static CCTV
    [InlineData("cam02")] // static CCTV (first validated capture)
    [InlineData("cctv01")] // PTZ camera
    [InlineData("turret01")] // auto-turret
    [InlineData("drone01")] // drone
    public void Render_RealCapturedFrames_MatchesApprovedGoldenImage(string fixtureId)
    {
        var fixture = JsonSerializer.Deserialize<CaptureFixture>(
                          File.ReadAllText(Path.Combine(FixturesDirectory, $"{fixtureId}-frames.json")))
                      ?? throw new InvalidOperationException($"{fixtureId}-frames.json deserialized to null");

        var renderer = new CameraRenderer(fixture.Width, fixture.Height);
        foreach (var frame in fixture.Frames)
        {
            renderer.AddRays(new CameraFrame
            {
                SampleOffset = frame.SampleOffset, RayData = Convert.FromBase64String(frame.RayDataBase64)
            });
        }

        using var rendered = Image.Load<Rgba32>(renderer.Render());
        using var golden = Image.Load<Rgba32>(Path.Combine(FixturesDirectory, $"{fixtureId}-golden.png"));

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

#pragma warning disable CA1812 // instantiated by JsonSerializer.Deserialize<T>
    private sealed record CapturedFrame(int SampleOffset, string RayDataBase64);

    private sealed record CaptureFixture(string CameraId, int Width, int Height, List<CapturedFrame> Frames);
#pragma warning restore CA1812
}
