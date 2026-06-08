using RustPlusApi.Camera;
using RustPlusApi.Data.Cameras;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using Xunit;

namespace RustPlusApi.Tests.Unit;

/// <summary>
/// Guards the deterministic parts of the camera ray decode + render (v2 §5a rendering layer).
/// End-to-end fidelity against a real frame is still pending the golden-payload capture (§15.4);
/// these tests lock the full-ray decode math, the sky sentinel and the material colouring.
/// </summary>
public class CameraRendererTests
{
    /// <summary>Full ray: 0xFF marker, then 3 bytes encoding distance/alignment/material.</summary>
    /// <param name="b1">High byte: distance[9:2].</param>
    /// <param name="b2">Middle byte: distance[1:0] in bits 7-6, alignment in bits 5-0.</param>
    /// <param name="b3">Material index.</param>
    /// <remarks>t = (b1 &lt;&lt; 2) | (b2 &gt;&gt; 6), alignment = b2 &amp; 0x3F, material = b3.</remarks>
    private static byte[] FullRay(byte b1, byte b2, byte b3) => [255, b1, b2, b3];

    private static Image<Rgba32> Decode(byte[] png) => Image.Load<Rgba32>(png);

    [Fact]
    public void Render_EmptyBuffer_ProducesImageOfRequestedSize()
    {
        var renderer = new CameraRenderer(32, 24);

        using var image = Decode(renderer.Render());

        Assert.Equal(32, image.Width);
        Assert.Equal(24, image.Height);
    }

    [Fact]
    public void SkySample_RendersSkyColour()
    {
        // t = 1023, alignment = 0, material = 0  ->  sky sentinel [208, 230, 252].
        // b1 = 255, b2 = 192 (>>6 == 3, &0x3F == 0), b3 = 0  ->  t = (255<<2)|3 = 1023.
        var renderer = new CameraRenderer(16, 16);
        var rays = new List<byte>();
        for (var n = 0; n < 32; n++)
            rays.AddRange(FullRay(255, 192, 0));

        renderer.AddRays(new CameraFrame { RayData = rays.ToArray(), SampleOffset = 0 });

        using var image = Decode(renderer.Render());
        Assert.True(HasPixel(image, new Rgba32(208, 230, 252)));
    }

    [Fact]
    public void TerrainSample_RendersMaterialColour()
    {
        // t = 512, alignment = 63 (full), material = 2 -> colour [0.3,0.7,1] * 255 = (76,178,255).
        // b1 = 128 (<<2 = 512), b2 = 63 (>>6 == 0, &0x3F == 63), b3 = 2.
        var renderer = new CameraRenderer(16, 16);
        var rays = new List<byte>();
        for (var n = 0; n < 32; n++)
            rays.AddRange(FullRay(128, 63, 2));

        renderer.AddRays(new CameraFrame { RayData = rays.ToArray(), SampleOffset = 0 });

        using var image = Decode(renderer.Render());
        Assert.True(HasPixel(image, new Rgba32(76, 178, 255)));
    }

    private static bool HasPixel(Image<Rgba32> image, Rgba32 colour)
    {
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                if (image[x, y] == colour)
                    return true;
            }
        }
        return false;
    }
}
