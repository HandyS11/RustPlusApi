using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace RustPlus.ConsoleApp.Utils;

/// <summary>
/// Prints a rendered camera PNG as ASCII art by downsampling to a console-sized grid and mapping
/// per-pixel luminance to a character ramp.
/// </summary>
internal static class CameraAsciiRenderer
{
    private const string Ramp = " .:-=+*#%@";
    private const int Columns = 80;
    private const int Rows = 40;

    public static void Print(byte[] pngBytes)
    {
        using var image = Image.Load<Rgba32>(pngBytes);
        image.Mutate(x => x.Resize(Columns, Rows));

        for (var y = 0; y < image.Height; y++)
        {
            var line = new char[image.Width];
            for (var x = 0; x < image.Width; x++)
            {
                var p = image[x, y];
                var luminance = ((0.2126 * p.R) + (0.7152 * p.G) + (0.0722 * p.B)) / 255.0;
                var index = (int)(luminance * (Ramp.Length - 1));
                line[x] = Ramp[index];
            }

            Console.WriteLine(new string(line));
        }
    }
}
