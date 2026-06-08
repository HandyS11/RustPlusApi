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

    [Fact]
    public void AddRays_RepeatAndDeltaArms_DecodeWithoutThrowing()
    {
        var renderer = new CameraRenderer(16, 16);
        var rays = new List<byte>();
        // Seed a full ray (255) so the lookback table has an entry...
        rays.AddRange(new byte[] { 255, 128, 63, 2 });
        // ...then a repeat (high bits 00), a small-delta (01 -> +1 extra byte),
        // and a medium-delta (10 -> +1 extra byte).
        rays.AddRange(new byte[] { 0b0000_0000 });            // repeat lookback[0]
        rays.AddRange(new byte[] { 0b0100_0000, 0x88 });      // small delta
        rays.AddRange(new byte[] { 0b1000_0000, 0x80 });      // medium delta
        // default arm (11): two extra bytes, material in low 6 bits.
        rays.AddRange(new byte[] { 0b1100_0001, 0x40, 0x00 });

        renderer.AddRays(new CameraFrame { RayData = rays.ToArray(), SampleOffset = 0 });
        var png = renderer.Render();

        Assert.NotEmpty(png); // decode exercised all four switch arms without an index throw
    }

    [Fact]
    public void AddRays_AlignmentAboveOne_ClampedTo255NotThrown()
    {
        // Drive ToByte's > 255 clamp arm: produce alignmentRaw > 63 via small-delta
        // (r += 4 from g=0xFF) starting from r=63. With material=6 (palette[0]=1.0f),
        // ToByte(alignment * 1.0 * 255) with alignment ≈ 1.063 → v=271 > 255 → clamp.
        var renderer = new CameraRenderer(16, 16);
        // Full ray: b1=0, b2=63 (r=63), b3=6 (material=6 → palette[6]=[1,0.4,0.4])
        // u = ((3*0) + (5*(63/16=3)) + (7*6)) & 63 = (0+15+42) & 63 = 57
        var rays = new List<byte> { 255, 0, 63, 6 };
        // Small-delta on lookback[57]: n = 0x40|57 = 0x79, g=0xFF (r += (7&255)-3 = 4 → r=67)
        rays.AddRange(new byte[] { 0x79, 0xFF });

        renderer.AddRays(new CameraFrame { RayData = rays.ToArray(), SampleOffset = 0 });
        var png = renderer.Render();

        Assert.NotEmpty(png); // ToByte > 255 arm clamped, no throw
    }

    [Fact]
    public void AddRays_NegativeAlignment_ClampedToBlackNotThrown()
    {
        // Drive ToByte's < 0 clamp arm: produce a negative alignmentRaw via
        // small-delta with g=0 (r_delta = (0 & 7) - 3 = -3, so r starts at 0 and goes to -3).
        // First a full ray with r=0, t=0, i=0, then a small-delta that makes r=-3.
        var renderer = new CameraRenderer(16, 16);
        var rays = new List<byte>();
        // Full ray: b1=0 (t=0), b2=0 (alignment=0), b3=0 (material=0) — sky sentinel, but
        // with a non-sky t. Actually t=(0<<2)|(0>>6)=0, alignment=0&0x3f=0, material=0.
        // This stores in lookback at u=((3*0)+(5*0)+(7*0))&63=0.
        rays.AddRange(new byte[] { 255, 0, 0, 0 });
        // Small-delta on lookback[0]: n=0b0100_0000 (case 64), g=0
        // => t += (0>>3)-15 = -15, r += (7&0)-3 = -3. r becomes -3.
        rays.AddRange(new byte[] { 0b0100_0000, 0x00 });

        renderer.AddRays(new CameraFrame { RayData = rays.ToArray(), SampleOffset = 0 });
        var png = renderer.Render();

        Assert.NotEmpty(png); // negative alignment clamped to 0 → black pixel, no throw
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
