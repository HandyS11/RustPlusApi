using RustPlusApi.Data.Cameras;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace RustPlusApi.Camera;

/// <summary>
/// Renders the Rust+ camera ray stream into images. Create one per subscribed camera
/// (sized from <see cref="CameraInfo"/>), feed each <see cref="CameraFrame"/> via
/// <see cref="AddRays"/>, then call <see cref="Render"/> for the current image.
/// </summary>
/// <remarks>
/// The ray decode, sample shuffle and colouring are ported from liamcottle/rustplus.js and
/// validated against frames captured from a live server (see the golden render test in
/// <c>RustPlusApi.Camera.UnitTests</c>, fixture captured 2026-06-12).
/// </remarks>
public sealed class CameraRenderer
{
    /// <summary>Largest width or height accepted. Sample positions are stored as <see cref="short"/>,
    /// so a coordinate of 32768 or more would truncate; real cameras are 160x90.</summary>
    private const int MaxDimension = 32768;

    /// <summary>Largest pixel count accepted, bounding the ~28-bytes-per-pixel allocation and the
    /// per-render work. 4096 x 4096 is far above any real camera.</summary>
    private const int MaxPixels = 4096 * 4096;

    private static readonly float[][] Colours =
    [
        [0.5f, 0.5f, 0.5f], [0.8f, 0.7f, 0.7f], [0.3f, 0.7f, 1f], [0.6f, 0.6f, 0.6f],
        [0.7f, 0.7f, 0.7f], [0.8f, 0.6f, 0.4f], [1f, 0.4f, 0.4f], [1f, 0.1f, 0.1f],
    ];

    private static readonly Rgba32 SkyColour = new(208, 230, 252);
    private readonly int _height;

    /// <summary>Raw decoded samples (distance 0-1023, alignment 0-63, material). Normalised at render time.</summary>
    private readonly (int Distance, int Alignment, int Material)?[] _output;

    private readonly short[] _samplePositionBuffer;

    private readonly int _width;

    /// <summary>Creates a renderer for a camera of the given dimensions.</summary>
    /// <param name="width">Camera image width in pixels, from <see cref="CameraInfo.Width"/>.</param>
    /// <param name="height">Camera image height in pixels, from <see cref="CameraInfo.Height"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> or
    /// <paramref name="height"/> is not positive or exceeds <see cref="MaxDimension"/>, or their
    /// product exceeds <see cref="MaxPixels"/>. The dimensions come from the server's camera
    /// subscribe reply, so they are untrusted: validating here keeps a hostile or on-path server
    /// from sizing an unbounded allocation.</exception>
    public CameraRenderer(int width, int height)
    {
        // Plain guards rather than ArgumentOutOfRangeException.ThrowIf*: those statics do not exist
        // on the netstandard2.0 target this library still ships.
        if (width is < 1 or > MaxDimension)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width), width, $"Camera width must be between 1 and {MaxDimension}.");
        }

        if (height is < 1 or > MaxDimension)
        {
            throw new ArgumentOutOfRangeException(
                nameof(height), height, $"Camera height must be between 1 and {MaxDimension}.");
        }

        if ((long)width * height > MaxPixels)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width), $"Camera dimensions {width}x{height} exceed the {MaxPixels}-pixel limit.");
        }

        _width = width;
        _height = height;
        _output = new (int, int, int)?[width * height];
        _samplePositionBuffer = BuildSamplePositionBuffer(width, height);
    }

    /// <summary>Decodes a frame's ray data and accumulates its samples into the image buffer.</summary>
    /// <param name="frame">The camera frame whose ray data will be decoded.</param>
    public void AddRays(CameraFrame frame)
    {
        var rayData = frame.RayData;
        // The frame's SampleOffset counts SAMPLES; the position buffer stores (x,y) PAIRS, so the
        // read cursor is twice the offset (reference: rustplus.js `2 * frame.sampleOffset`).
        // Un-doubled it lands on the wrong positions — and de-pairs the reads when odd —
        // scrambling the whole image into noise.
        var sampleOffset = 2 * frame.SampleOffset;

        var lookback = new int[64][];
        for (var k = 0; k < lookback.Length; k++)
        {
            lookback[k] = new int[3];
        }

        var p = 0;
        while (p < rayData.Length - 1)
        {
            int t, r, i;
            int n = rayData[p++];

            if (n == 255)
            {
                int l = rayData[p++], o = rayData[p++], s = rayData[p++];
                t = (l << 2) | (o >> 6);
                r = 63 & o;
                i = s;
                Store(lookback, t, r, i);
            }
            else
            {
                switch (192 & n)
                {
                    case 0:
                        Load(lookback[63 & n], out t, out r, out i);
                        break;
                    case 64:
                        Load(lookback[63 & n], out t, out r, out i);
                        var g = rayData[p++];
                        t += (g >> 3) - 15;
                        r += (7 & g) - 3;
                        break;
                    case 128:
                        Load(lookback[63 & n], out t, out r, out i);
                        t += rayData[p++] - 127;
                        break;
                    default:
                        int a = rayData[p++], f = rayData[p++];
                        t = (a << 2) | (f >> 6);
                        r = 63 & f;
                        i = 63 & n;
                        Store(lookback, t, r, i);
                        break;
                }
            }

            sampleOffset %= 2 * _width * _height;
            // Pair-aligned reads (the cursor starts even and advances by 2) always yield
            // x < width and y < height, so the index is in range by construction.
            var index = _samplePositionBuffer[sampleOffset++] + (_samplePositionBuffer[sampleOffset++] * _width);
            _output[index] = (t, r, i);
        }
    }

    /// <summary>Renders the accumulated samples to a PNG image.</summary>
    public byte[] Render()
    {
        using var image = new Image<Rgba32>(_width, _height);

        for (var i = 0; i < _output.Length; i++)
        {
            var ray = _output[i];
            if (ray is null)
            {
                continue;
            }

            var (distance, alignmentRaw, material) = ray.Value;

            Rgba32 colour;
            // Sky sentinel: distance == 1, alignment == 0, material == 0 (in normalized terms).
            if (distance == 1023 && alignmentRaw == 0 && material == 0)
            {
                colour = SkyColour;
            }
            else
            {
                var alignment = alignmentRaw / 63f;
                var palette = Colours[material % Colours.Length];
                colour = new Rgba32(
                    ToByte(alignment * palette[0] * 255f),
                    ToByte(alignment * palette[1] * 255f),
                    ToByte(alignment * palette[2] * 255f));
            }

            var x = i % _width;
            var y = _height - 1 - (i / _width);
            image[x, y] = colour;
        }

        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    private static void Store(int[][] lookback, int t, int r, int i)
    {
        var u = ((3 * (t / 128)) + (5 * (r / 16)) + (7 * i)) & 63;
        lookback[u][0] = t;
        lookback[u][1] = r;
        lookback[u][2] = i;
    }

    private static void Load(int[] entry, out int t, out int r, out int i)
    {
        t = entry[0];
        r = entry[1];
        i = entry[2];
    }

    private static byte ToByte(float value)
    {
        var v = (int)value;
        return v switch
        {
            < 0 => 0,
            > 255 => 255,
            _ => (byte)v
        };
    }

    private static short[] BuildSamplePositionBuffer(int width, int height)
    {
        var buffer = new short[width * height * 2];
        for (int w = 0, y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                buffer[w++] = (short)x;
                buffer[w++] = (short)y;
            }
        }

        var generator = new IndexGenerator(1337);
        for (var rIndex = (width * height) - 1; rIndex >= 1; rIndex--)
        {
            var c = 2 * rIndex;
            var swap = 2 * generator.NextInt(rIndex + 1);

            (buffer[swap], buffer[c]) = (buffer[c], buffer[swap]);
            (buffer[swap + 1], buffer[c + 1]) = (buffer[c + 1], buffer[swap + 1]);
        }

        return buffer;
    }
}
