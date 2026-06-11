using RustPlusApi.Camera;
using RustPlusApi.Data.Cameras;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace RustPlusApi.Camera.UnitTests;

/// <summary>
/// Guards the deterministic parts of the camera ray decode + render.
/// Tests use EXACT (pinned / characterization) assertions so that arithmetic mutations in
/// CameraRenderer and IndexGenerator change observable output and are killed by Stryker.
/// <para>
/// Coordinate conventions:
///   <list type="bullet">
///     <item>The 4×4 renderer seeded with IndexGenerator(1337) maps sample[0] to the
///           buffer position (x=2,y=1) → linear index 6 → image pixel (2, 2)
///           (the renderer flips Y: image_y = height-1-(linear/width)).</item>
///   </list>
/// </para>
/// End-to-end fidelity against a real captured frame is still pending the golden-payload
/// capture; these tests lock the full-ray decode math, the sky sentinel,
/// the material colouring, the lookback table, and all four switch arms.
/// </summary>
public class CameraRendererTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>Full ray: 0xFF marker, then 3 bytes encoding distance/alignment/material.</summary>
    /// <param name="b1">High byte: distance[9:2].</param>
    /// <param name="b2">Middle byte: distance[1:0] in bits 7-6, alignment in bits 5-0.</param>
    /// <param name="b3">Material index.</param>
    /// <remarks>t = (b1 &lt;&lt; 2) | (b2 &gt;&gt; 6), alignment = b2 &amp; 0x3F, material = b3.</remarks>
    private static byte[] FullRay(byte b1, byte b2, byte b3) => [255, b1, b2, b3];

    private static Image<Rgba32> Decode(byte[] png) => Image.Load<Rgba32>(png);

    // ──────────────────────────────────────────────────────────────────────────
    // Basic structural checks
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Render_EmptyBuffer_ProducesImageOfRequestedSize()
    {
        var renderer = new CameraRenderer(32, 24);

        using var image = Decode(renderer.Render());

        Assert.Equal(32, image.Width);
        Assert.Equal(24, image.Height);
    }

    [Fact]
    public void Render_EmptyBuffer_AllPixelsTransparent()
    {
        var renderer = new CameraRenderer(4, 4);
        using var image = Decode(renderer.Render());

        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                Assert.Equal(new Rgba32(0, 0, 0, 0), image[x, y]);
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Full-ray decode: exact pixel at exact coordinate
    //
    // The 4×4 renderer's sample shuffle (seeded IndexGenerator(1337)) puts
    // sample[0] at buffer entry (x=2, y=1) → linear index 6 → image pixel (2, 2).
    // Encoding: b1=128 → t=(128<<2)|(63>>6)=512, b2=63 → r=63, b3=2 → mat=2
    //   colour = palette[2]*alignment = [0.3,0.7,1.0] * (63/63) * 255 = (76,178,255)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FullRay_SingleRay_ExactPixelAtKnownCoordinate()
    {
        var renderer = new CameraRenderer(4, 4);
        // One full ray: t=512, r=63 (alignment=1.0), material=2
        renderer.AddRays(new CameraFrame
        {
            RayData = FullRay(128, 63, 2), SampleOffset = 0
        });

        using var image = Decode(renderer.Render());

        // sample[0] in 4×4 → image(2,2) painted with material-2 full-alignment colour
        Assert.Equal(new Rgba32(76, 178, 255), image[2, 2]);
    }

    [Fact]
    public void FullRay_SingleRay_NeighborPixelRemainsTransparent()
    {
        var renderer = new CameraRenderer(4, 4);
        renderer.AddRays(new CameraFrame
        {
            RayData = FullRay(128, 63, 2), SampleOffset = 0
        });

        using var image = Decode(renderer.Render());

        // Every pixel except (2,2) must remain transparent
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                if (x != 2 || y != 2)
                {
                    Assert.Equal(new Rgba32(0, 0, 0, 0), image[x, y]);
                }
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Sky sentinel: t==1023, alignment==0, material==0 → exact sky colour
    //
    // b1=255, b2=192 → t=(255<<2)|(192>>6)=1023, r=0, b3=0.
    // sample[0] in 16×16 → image(14, 0).
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SkySample_ExactPixelCoordinate_SkyColour()
    {
        var renderer = new CameraRenderer(16, 16);
        renderer.AddRays(new CameraFrame
        {
            RayData = FullRay(255, 192, 0), SampleOffset = 0
        });

        using var image = Decode(renderer.Render());

        // sample[0] in 16×16 → image(14, 0) = sky
        Assert.Equal(new Rgba32(208, 230, 252), image[14, 0]);
    }

    [Fact]
    public void SkySample_RendersSkyColour()
    {
        // t = 1023, alignment = 0, material = 0 → sky sentinel [208, 230, 252].
        var renderer = new CameraRenderer(16, 16);
        var rays = new List<byte>();
        for (var n = 0; n < 32; n++)
        {
            rays.AddRange(FullRay(255, 192, 0));
        }

        renderer.AddRays(new CameraFrame
        {
            RayData = [.. rays], SampleOffset = 0
        });

        using var image = Decode(renderer.Render());
        Assert.True(HasPixel(image, new Rgba32(208, 230, 252)));
    }

    [Fact]
    public void SkySample_NeighborPixelRemainsTransparent_InSingleRayRender()
    {
        var renderer = new CameraRenderer(16, 16);
        renderer.AddRays(new CameraFrame
        {
            RayData = FullRay(255, 192, 0), SampleOffset = 0
        });

        using var image = Decode(renderer.Render());

        // (14,0) is sky; (13,0) and (14,1) must be transparent
        Assert.Equal(new Rgba32(0, 0, 0, 0), image[13, 0]);
        Assert.Equal(new Rgba32(0, 0, 0, 0), image[14, 1]);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Material palette: exact colour for every material at full alignment (r=63)
    // Each assertion kills the corresponding palette constant mutation.
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 127, 127, 127)] // [0.5, 0.5, 0.5]
    [InlineData(1, 204, 178, 178)] // [0.8, 0.7, 0.7]
    [InlineData(2, 76, 178, 255)] // [0.3, 0.7, 1.0]
    [InlineData(3, 153, 153, 153)] // [0.6, 0.6, 0.6]
    [InlineData(4, 178, 178, 178)] // [0.7, 0.7, 0.7]
    [InlineData(5, 204, 153, 102)] // [0.8, 0.6, 0.4]
    [InlineData(6, 255, 102, 102)] // [1.0, 0.4, 0.4]
    [InlineData(7, 255, 25, 25)] // [1.0, 0.1, 0.1]
    public void Material_FullAlignment_ExactPixelColour(int material, byte r, byte g, byte b)
    {
        // b2=63 → alignment=63, t=(128<<2)|(63>>6)=512 (non-sky)
        var renderer = new CameraRenderer(4, 4);
        renderer.AddRays(new CameraFrame
        {
            RayData = FullRay(128, 63, (byte)material), SampleOffset = 0
        });

        using var image = Decode(renderer.Render());
        // sample[0] in 4×4 → image(2,2)
        Assert.Equal(new Rgba32(r, g, b), image[2, 2]);
    }

    [Fact]
    public void TerrainSample_RendersMaterialColour()
    {
        // t = 512, alignment = 63 (full), material = 2 → colour [0.3,0.7,1] * 255 = (76,178,255).
        var renderer = new CameraRenderer(16, 16);
        var rays = new List<byte>();
        for (var n = 0; n < 32; n++)
        {
            rays.AddRange(FullRay(128, 63, 2));
        }

        renderer.AddRays(new CameraFrame
        {
            RayData = [.. rays], SampleOffset = 0
        });

        using var image = Decode(renderer.Render());
        Assert.True(HasPixel(image, new Rgba32(76, 178, 255)));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Alignment scaling: exact colours at partial alignment (r=32 → 32/63)
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 64, 64, 64)] // [0.5,0.5,0.5] * 32/63
    [InlineData(1, 103, 90, 90)] // [0.8,0.7,0.7] * 32/63
    [InlineData(2, 38, 90, 129)] // [0.3,0.7,1.0] * 32/63
    [InlineData(5, 103, 77, 51)] // [0.8,0.6,0.4] * 32/63
    [InlineData(7, 129, 12, 12)] // [1.0,0.1,0.1] * 32/63
    public void Material_MidAlignment_ExactPixelColour(int material, byte r, byte g, byte b)
    {
        // b2=32 → alignment=32, t=(0<<2)|(32>>6)=0 → not sky (t≠1023)
        // b1 must encode t ≠ 1023; use b1=1 → t=(1<<2)|(32>>6)=4, r=32
        var renderer = new CameraRenderer(4, 4);
        renderer.AddRays(new CameraFrame
        {
            // b1=1 → t=4; b2=32 → r=32; material as given
            RayData = FullRay(1, 32, (byte)material), SampleOffset = 0
        });

        using var image = Decode(renderer.Render());
        Assert.Equal(new Rgba32(r, g, b), image[2, 2]);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // ToByte clamp: > 255 → 255
    //
    // r=67 → alignment=67/63 ≈ 1.063; material=6 ([1,0.4,0.4])
    //   R = int(1.063*1.0*255)=271 → clamped 255
    //   G = int(1.063*0.4*255)=108 → 108
    //   B = int(1.063*0.4*255)=108 → 108
    // Drive r=67: base full ray (b2=63 → r=63) then small-delta n=(64|41)=105, g=(0x80)
    //   delta: t+=1 → t=513, r-=3 → r=60. Not quite 67.
    // Instead use default arm (11) encoding r directly:
    //   n=0b1100_0001=193, a=0x00, f=67 → t=0, r=63&67=3... that's wrong.
    // Use a fresh full ray with b2 encoding r=67 directly:
    //   b2 has bits[7:6]=distance_low and bits[5:0]=alignment; alignment = b2 & 0x3F.
    //   b2=67 = 0x43 = 0b0100_0011: bits[7:6]=01=1, bits[5:0]=3 → r=3. Not 67.
    //   We can't encode r>63 in b2 (only 6 bits). Use small-delta to get r>63:
    //   Start r=63, delta g=0xFF: r += (7&255)-3 = 7-3=4 → r=67. ✓
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ToByte_OverflowClamp_ExactPixelColour()
    {
        // Full ray: b1=0, b2=63 (r=63), b3=6 → t=0, r=63, i=6
        //   t=0: not sky (t≠1023). stored at u=((3*0)+(5*3)+(7*6))&63=(0+15+42)&63=57
        // Small-delta: n=(64|57)=121=0x79, g=0xFF → t+=16-15=1, r+=7-3=4 → r=67
        //   alignment=67/63≈1.063, mat=6 → R=ToByte(271)=255, G=ToByte(108)=108, B=108
        // sample[0] in 4×4 → image(2,2); sample[1] → image(3,0)
        var renderer = new CameraRenderer(4, 4);
        renderer.AddRays(new CameraFrame
        {
            RayData = [255, 0, 63, 6, 0x79, 0xFF], SampleOffset = 0
        });

        using var image = Decode(renderer.Render());
        Assert.Equal(new Rgba32(255, 102, 102), image[2, 2]); // full ray r=63 mat=6: (255,102,102)
        Assert.Equal(new Rgba32(255, 108, 108), image[3, 0]); // delta ray r=67 mat=6: R clamped, G/B=108
    }

    [Fact]
    public void AddRays_AlignmentAboveOne_ClampedTo255NotThrown()
    {
        // Drive ToByte's > 255 clamp arm: produce alignmentRaw > 63 via small-delta.
        var renderer = new CameraRenderer(16, 16);
        var rays = new List<byte>
        {
            255, 0, 63, 6
        };
        rays.AddRange([0x79, 0xFF]);

        renderer.AddRays(new CameraFrame
        {
            RayData = [.. rays], SampleOffset = 0
        });
        var png = renderer.Render();

        Assert.NotEmpty(png);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // ToByte clamp: < 0 → 0
    //
    // Full ray r=0, then small-delta g=0 → r += (0&7)-3=-3 → r=-3
    //   alignment=-3/63<0 → ToByte(neg)=0 → black pixel.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ToByte_NegativeAlignment_ClampedToBlack()
    {
        // Full ray: b1=0, b2=0, b3=0 → t=0, r=0, i=0 stored at u=0
        // Small-delta: n=64 (case64, lookback[0]), g=0 → t-=15, r-=3 → r=-3
        // mat=0 [0.5,0.5,0.5], R=ToByte(-3/63*0.5*255)=ToByte(-6.07)=0
        // sample[0] in 4×4 → image(2,2) (full ray); sample[1] → image(3,0) (delta)
        var renderer = new CameraRenderer(4, 4);
        renderer.AddRays(new CameraFrame
        {
            RayData = [255, 0, 0, 0, 0b0100_0000, 0x00], SampleOffset = 0
        });

        using var image = Decode(renderer.Render());
        // Full ray: t=0, r=0 → alignment=0 → black (0,0,0)
        Assert.Equal(new Rgba32(0, 0, 0), image[2, 2]);
        // Delta ray: r=-3 → clamped black (0,0,0)
        Assert.Equal(new Rgba32(0, 0, 0), image[3, 0]);
    }

    [Fact]
    public void AddRays_NegativeAlignment_ClampedToBlackNotThrown()
    {
        var renderer = new CameraRenderer(16, 16);
        var rays = new List<byte>();
        rays.AddRange([255, 0, 0, 0]);
        rays.AddRange("@\0"u8.ToArray());

        renderer.AddRays(new CameraFrame
        {
            RayData = [.. rays], SampleOffset = 0
        });
        var png = renderer.Render();

        Assert.NotEmpty(png);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Four switch arms — exact pixel colours at known coordinates
    //
    // 4×4 renderer, sampleOffset=0:
    //   sample[0] → image(2,2)   ray 0: full ray 255 header
    //   sample[1] → image(3,0)   ray 1: repeat arm (00 high bits)
    //   sample[2] → image(0,1)   ray 2: small-delta arm (01 high bits)
    //   sample[3] → image(0,3)   ray 3: medium-delta arm (10 high bits)
    //   sample[4] → image(0,2)   ray 4: default arm (11 high bits)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AllFourSwitchArms_ExactPixelColours()
    {
        // ── Ray 0: Full ray ───────────────────────────────────────────────────
        // b1=128, b2=63, b3=2 → t=512, r=63, i=2
        //   u=((3*(512/128))+(5*(63/16))+(7*2))&63=(12+15+14)&63=41
        //   colour mat2 r=63: (76,178,255) → image(2,2)
        //
        // ── Ray 1: Repeat arm (case 0) ────────────────────────────────────────
        //   n=41=0b0010_1001: 192&41=0 → case 0; 63&41=41 → loads lookback[41]={512,63,2}
        //   same colour (76,178,255) → image(3,0)
        //
        // ── Ray 2: Small-delta arm (case 64) ─────────────────────────────────
        //   n=105=(64|41)=0x69: 192&105=64 → case 64; 63&105=41 → loads lookback[41]
        //   g=0x80=128: t+=(128>>3)-15=1 → t=513; r+=(7&128)-3=-3 → r=60
        //   mat2 r=60: alignment=60/63, (76*60/63,178*60/63,255*60/63) = (72,170,242) → image(0,1)
        //
        // ── Ray 3: Medium-delta arm (case 128) ───────────────────────────────
        //   n=169=(128|41)=0xA9: 192&169=128 → case 128; 63&169=41 → loads lookback[41]
        //   g=128: t+=128-127=1 → t=513; r stays 63
        //   mat2 r=63: (76,178,255) → image(0,3)
        //
        // ── Ray 4: Default arm (case 192) ────────────────────────────────────
        //   n=0b1100_0001=193: 192&193=192 → default; i=63&193=1
        //   a=0x40=64, f=0x00: t=(64<<2)|(0>>6)=256, r=63&0=0
        //   u=((3*(256/128))+(5*(0/16))+(7*1))&63=(6+0+7)&63=13
        //   alignment=0/63=0 → ToByte(0)=0 → black (0,0,0) → image(0,2)

        var rayData = new byte[]
        {
            255, 128, 63, 2, // ray 0: full ray
            41, // ray 1: repeat lookback[41]
            105, 0x80, // ray 2: small-delta on lookback[41], g=0x80
            169, 128, // ray 3: medium-delta on lookback[41], g=128
            0b1100_0001, 0x40, 0x00, // ray 4: default arm
        };

        var renderer = new CameraRenderer(4, 4);
        renderer.AddRays(new CameraFrame
        {
            RayData = rayData, SampleOffset = 0
        });

        using var image = Decode(renderer.Render());

        Assert.Equal(new Rgba32(76, 178, 255), image[2, 2]); // ray 0: full ray
        Assert.Equal(new Rgba32(76, 178, 255), image[3, 0]); // ray 1: repeat → same colour
        Assert.Equal(new Rgba32(72, 170, 242), image[0, 1]); // ray 2: small-delta, r=60
        Assert.Equal(new Rgba32(76, 178, 255), image[0, 3]); // ray 3: medium-delta, r=63
        Assert.Equal(new Rgba32(0, 0, 0), image[0, 2]); // ray 4: default, r=0 → black
    }

    [Fact]
    public void AddRays_RepeatAndDeltaArms_DecodeWithoutThrowing()
    {
        var renderer = new CameraRenderer(16, 16);
        var rays = new List<byte>();
        rays.AddRange([255, 128, 63, 2]);
        rays.AddRange([0b0000_0000]);
        rays.AddRange([0b0100_0000, 0x88]);
        rays.AddRange([0b1000_0000, 0x80]);
        rays.AddRange([0b1100_0001, 0x40, 0x00]);

        renderer.AddRays(new CameraFrame
        {
            RayData = [.. rays], SampleOffset = 0
        });
        var png = renderer.Render();

        Assert.NotEmpty(png);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Lookback Store/Load hash: exact slot computation
    //
    // u = ((3*(t/128)) + (5*(r/16)) + (7*i)) & 63
    // After storing, a repeat arm using n=u (case 0) must reproduce the same colour.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void LookbackHash_RepeatLoadsExactStoredValues()
    {
        // t=512, r=32, i=5:
        //   u=((3*(512/128))+(5*(32/16))+(7*5))&63=(12+10+35)&63=57
        // Full ray at sampleOffset=0 → image(2,2) with mat5, r=32: (103,77,51)
        // Repeat n=57=0b0011_1001 → case 0, loads lookback[57] → same colour at image(3,0)

        // Encode: t=512 → b1=128(t>>2=128), lo2=(512&3)=0 → b2=32|(0<<6)=32, b3=5
        var renderer = new CameraRenderer(4, 4);
        renderer.AddRays(new CameraFrame
        {
            // Full ray + repeat referencing slot 57; trailing 0 ensures the loop's
            // "p < rayData.Length - 1" guard still includes the repeat byte.
            RayData = [255, 128, 32, 5, 57, 0], SampleOffset = 0
        });

        using var image = Decode(renderer.Render());

        var mat5r32 = new Rgba32(103, 77, 51);
        Assert.Equal(mat5r32, image[2, 2]); // full ray
        Assert.Equal(mat5r32, image[3, 0]); // repeat → same slot → same colour
    }

    // ──────────────────────────────────────────────────────────────────────────
    // SampleOffset: non-zero offset skips buffer entries
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AddRays_NonZeroSampleOffset_PaintsCorrectPixel()
    {
        // sampleOffset=2 skips sample[0] and starts at sample[1] in 4×4.
        // sample[1] = linear 15 → image(3, 0).
        var renderer = new CameraRenderer(4, 4);
        renderer.AddRays(new CameraFrame
        {
            RayData = FullRay(128, 63, 0), // mat0, r=63 → (127,127,127)
            SampleOffset = 2
        });

        using var image = Decode(renderer.Render());

        Assert.Equal(new Rgba32(127, 127, 127), image[3, 0]);
        Assert.Equal(new Rgba32(0, 0, 0, 0), image[2, 2]); // sample[0] not touched
    }

    // ──────────────────────────────────────────────────────────────────────────
    // SampleOffset wrap-around (% 2*width*height)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AddRays_SampleOffsetWraps_PaintsCorrectPixel()
    {
        // 4×4: 2*width*height = 32. sampleOffset=32 wraps to 0 → same as sampleOffset=0.
        var renderer1 = new CameraRenderer(4, 4);
        renderer1.AddRays(new CameraFrame
        {
            RayData = FullRay(128, 63, 2), SampleOffset = 0
        });

        var renderer2 = new CameraRenderer(4, 4);
        renderer2.AddRays(new CameraFrame
        {
            RayData = FullRay(128, 63, 2), SampleOffset = 32
        });

        using var img1 = Decode(renderer1.Render());
        using var img2 = Decode(renderer2.Render());

        // Both should paint the same pixel
        Assert.Equal(img1[2, 2], img2[2, 2]);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Multiple frames accumulate (later ray overwrites earlier)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MultipleFrames_LaterFrameOverwritesEarlierRay()
    {
        var renderer = new CameraRenderer(4, 4);
        // Frame 1: paint image(2,2) with mat2 (76,178,255)
        renderer.AddRays(new CameraFrame
        {
            RayData = FullRay(128, 63, 2), SampleOffset = 0
        });
        // Frame 2: repaint image(2,2) with mat0 (127,127,127)
        renderer.AddRays(new CameraFrame
        {
            RayData = FullRay(128, 63, 0), SampleOffset = 0
        });

        using var image = Decode(renderer.Render());

        Assert.Equal(new Rgba32(127, 127, 127), image[2, 2]);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Y-axis flip: linear index i → image_y = height-1-(i/width)
    // Verify for two known pixels that the flip is applied.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Render_YAxisFlip_LinearIndex0MapsToBottomRow()
    {
        // sample[3] in 4×4 → linear=0 → x=0, image_y=height-1-0=3 → image(0,3)
        var renderer = new CameraRenderer(4, 4);
        // Need to use sampleOffset=6 so the ray hits sample[3]
        renderer.AddRays(new CameraFrame
        {
            RayData = FullRay(128, 63, 3), // mat3, r=63 → (153,153,153)
            SampleOffset = 6
        });

        using var image = Decode(renderer.Render());

        Assert.Equal(new Rgba32(153, 153, 153), image[0, 3]);
        Assert.Equal(new Rgba32(0, 0, 0, 0), image[0, 0]); // top-left should be untouched
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Material modulo wrapping (material % Colours.Length where Length=8)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Material_Modulo8_WrapsSamePalette()
    {
        // mat=10 should map to same palette as mat=2 (10%8=2)
        var r1 = new CameraRenderer(4, 4);
        r1.AddRays(new CameraFrame
        {
            RayData = FullRay(128, 63, 2), SampleOffset = 0
        });

        var r2 = new CameraRenderer(4, 4);
        r2.AddRays(new CameraFrame
        {
            RayData = FullRay(128, 63, 10), SampleOffset = 0
        });

        using var img1 = Decode(r1.Render());
        using var img2 = Decode(r2.Render());

        Assert.Equal(img1[2, 2], img2[2, 2]);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Loop bound: p < rayData.Length - 1  vs  p <= rayData.Length - 1  (#6)
    //
    // With the correct `<` bound, the very last byte of RayData is not read as `n`.
    // With the `<=` mutation it IS read, consuming the final byte as a repeat ray
    // (case 0 is safe — no out-of-bounds access).  We craft a frame where that
    // last byte would paint a different pixel to distinguish the two behaviours.
    //
    // Frame: [255, 128, 63, 2, 41] (5 bytes)
    //   Normal (p < 4): processes full-ray only → image(2,2) = (76,178,255), image(3,0) transparent.
    //   Mutant (p ≤ 4): additionally reads byte 41 as n, loads lookback[41] = {512,63,2},
    //                   paints image(3,0) = (76,178,255) too.
    // Assert image(3,0) is transparent kills the mutant.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void LoopBound_LastByteNotConsumedAsRay()
    {
        var renderer = new CameraRenderer(4, 4);
        renderer.AddRays(new CameraFrame
        {
            // Full ray that stores lookback[41], followed by repeat byte 41
            // which should NOT be consumed under the correct loop bound.
            RayData = [255, 128, 63, 2, 41], SampleOffset = 0
        });

        using var image = Decode(renderer.Render());

        Assert.Equal(new Rgba32(76, 178, 255), image[2, 2]); // full ray always paints this
        Assert.Equal(new Rgba32(0, 0, 0, 0), image[3, 0]); // repeat NOT consumed → transparent
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Default arm Store+Repeat: kills #40 (p--), #41 (&), #42 (a>>2), #43 (a>>>2),
    // #44 (f<<6), #47 (63|n), #48 (Store removed)
    //
    // Strategy: default arm (n=0b11000010=194, a=0x80, f=0xFF) computes
    //   t=(0x80<<2)|(0xFF>>6)=515, r=63&0xFF=63, i=63&194=2
    //   u=((3*(515/128))+(5*(63/16))+(7*2))&63 = (12+15+14)&63 = 41
    // The Store at slot 41 is then loaded by repeat n=41.
    // Any mutation that alters t (changing u) or skips Store or corrupts i
    // will cause slot 41 to remain {0,0,0} → repeat gives black → test fails.
    //
    // sample[0] in 4×4 → image(2,2)  (default arm ray)
    // sample[1] in 4×4 → image(3,0)  (repeat ray)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DefaultArm_StoreAndRepeat_ExactPixelColours()
    {
        // Bytes: [194, 0x80, 0xFF, 41, 0, 0]
        //   ray 0: default arm n=194, a=0x80, f=0xFF → t=515,r=63,i=2 → stored at slot 41
        //            image(2,2) = mat2 r=63 → (76,178,255)
        //   ray 1: repeat n=41 → loads slot 41 → same colour → image(3,0) = (76,178,255)
        //   ray 2: repeat n=0 → loads slot 0 (empty) → black → image(0,1) = (0,0,0)
        var renderer = new CameraRenderer(4, 4);
        renderer.AddRays(new CameraFrame
        {
            RayData = [194, 0x80, 0xFF, 41, 0, 0], SampleOffset = 0
        });

        using var image = Decode(renderer.Render());

        Assert.Equal(new Rgba32(76, 178, 255), image[2, 2]); // default arm
        Assert.Equal(new Rgba32(76, 178, 255), image[3, 0]); // repeat from correct slot
        Assert.Equal(new Rgba32(0, 0, 0), image[0, 1]); // repeat from empty slot 0
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Small-delta (case 64) sky path: kills #28 (t-=), #29 (+15), #30 (g<<3)
    //
    // Full ray seeds lookback slot 21 with {t=1022, r=0, i=0}.
    // Small-delta n=85=(64|21), g=131 (0x83):
    //   Normal:  t += (131>>3)-15 = 16-15 = +1 → t=1023, r += (7&131)-3=0 → r=0  → SKY
    //   Mut #28: t -= 1 → t=1021                                                   → black
    //   Mut #29: t += (16+15)=31 → t=1053                                           → black
    //   Mut #30: t += (131<<3)-15=1033 → t=2055                                    → black
    // sample[0] 4×4 → image(2,2) (full ray, r=0 → black); sample[1] → image(3,0) (delta → sky)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SmallDelta_DistanceReachesSkySentinel_ExactPixelColour()
    {
        // Full ray: 255, 255, 128, 0 → t=(255<<2)|(128>>6)=1022, r=63&128=0, i=0 → stored at u=21
        // Small-delta: n=85=(64|21), g=131 (0x83) → t+=1 → t=1023, r+=0 → r=0, i=0 → SKY
        var renderer = new CameraRenderer(4, 4);
        renderer.AddRays(new CameraFrame
        {
            RayData = [255, 255, 128, 0, 85, 131, 0], SampleOffset = 0
        });

        using var image = Decode(renderer.Render());

        Assert.Equal(new Rgba32(0, 0, 0), image[2, 2]); // full ray: r=0 → black
        Assert.Equal(new Rgba32(208, 230, 252), image[3, 0]); // delta hits sky sentinel
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Medium-delta (case 128) sky path: kills #36 (t-=), #37 (+127)
    //
    // Full ray seeds lookback slot 18 with {t=895, r=0, i=0}.
    // Medium-delta n=146=(128|18), g=255:
    //   Normal:  t += 255-127 = +128 → t=1023, r=0, i=0 → SKY
    //   Mut #36: t -= 128 → t=767                         → black
    //   Mut #37: t += 255+127 = +382 → t=1277             → black
    // sample[0] → image(2,2) (full ray, r=0 → black); sample[1] → image(3,0) (delta → sky)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MediumDelta_DistanceReachesSkySentinel_ExactPixelColour()
    {
        // Full ray: 255, 223, 192, 0 → t=(223<<2)|(192>>6)=895, r=63&192=0, i=0 → stored at u=18
        // Medium-delta: n=146=(128|18), g=255 → t+=128 → t=1023, r=0, i=0 → SKY
        var renderer = new CameraRenderer(4, 4);
        renderer.AddRays(new CameraFrame
        {
            RayData = [255, 223, 192, 0, 146, 255, 0], SampleOffset = 0
        });

        using var image = Decode(renderer.Render());

        Assert.Equal(new Rgba32(0, 0, 0), image[2, 2]); // full ray: r=0 → black
        Assert.Equal(new Rgba32(208, 230, 252), image[3, 0]); // delta hits sky sentinel
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Malformed-frame robustness: SampleOffset is server/network-supplied.
    // An odd offset swaps the (x,y) read roles so a sample can map outside the
    // image buffer; AddRays must drop it (bounds guard) rather than throw.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AddRays_OddSampleOffset_DropsOutOfRangeSamplesWithoutThrowing()
    {
        // 4×2 (width > height) + odd offset makes some samples map to an index
        // >= width*height (out of range). Seven rays guarantee at least one such
        // sample (the x-component read reaches 2 or 3), exercising the guard's
        // false branch. The offset stays below the buffer boundary, so the only
        // thing under test is the out-of-range image-index guard.
        var renderer = new CameraRenderer(4, 2);
        var rays = new List<byte>();
        for (var n = 0; n < 7; n++)
        {
            rays.AddRange(FullRay(128, 63, 2));
        }

        var exception = Record.Exception(() =>
            renderer.AddRays(new CameraFrame
            {
                RayData = [.. rays], SampleOffset = 1
            }));

        Assert.Null(exception);
        using var image = Decode(renderer.Render());
        Assert.Equal(4, image.Width);
        Assert.Equal(2, image.Height);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Private helper
    // ──────────────────────────────────────────────────────────────────────────

    private static bool HasPixel(Image<Rgba32> image, Rgba32 colour)
    {
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                if (image[x, y] == colour)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
