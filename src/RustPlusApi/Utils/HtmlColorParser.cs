using System.Drawing;
using System.Globalization;

namespace RustPlusApi.Utils;

/// <summary>
/// Parses Rust+ HTML colour strings into <see cref="Color"/>. On net10 it defers to the
/// in-box <c>ColorTranslator</c>; on netstandard2.0 (where <c>ColorTranslator</c> lives in
/// the Windows-only <c>System.Drawing.Common</c>) it parses hex colours itself, keeping the
/// core package dependency-light and cross-platform.
/// </summary>
public static class HtmlColorParser
{
    /// <summary>Parses an HTML colour string (e.g. <c>#RGB</c>, <c>#RRGGBB</c>, <c>#AARRGGBB</c>, or a named colour) into a <see cref="Color"/>.</summary>
    /// <param name="html">The HTML colour string to parse.</param>
    public static Color FromHtml(string html)
    {
#if NET10_0_OR_GREATER
        return ColorTranslator.FromHtml(html);
#else
        if (string.IsNullOrEmpty(html)) return Color.Empty;

        if (html[0] == '#')
        {
            var hex = html.Substring(1);
            switch (hex.Length)
            {
                case 3:
                    return Color.FromArgb(
                        Nibble(hex[0]), Nibble(hex[1]), Nibble(hex[2]));
                case 6:
                    var rgb = int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    return Color.FromArgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
                case 8:
                    var argb = uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    return Color.FromArgb(
                        (byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
            }
        }

        return Color.FromName(html);

        static int Nibble(char c)
        {
            var v = Convert.ToInt32(c.ToString(), 16);
            return (v << 4) | v;
        }
#endif
    }
}
