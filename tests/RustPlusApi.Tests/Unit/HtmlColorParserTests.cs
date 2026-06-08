using System.Drawing;
using RustPlusApi.Utils;
using Xunit;

namespace RustPlusApi.Tests.Unit;

/// <summary>Pins <see cref="HtmlColorParser.FromHtml"/> to identical ARGB output on both the
/// net10 (ColorTranslator) and netstandard2.0 (hand-rolled hex) code paths. Running under
/// net8.0 exercises the netstandard build; under net10.0 the in-box translator.</summary>
public class HtmlColorParserTests
{
    [Fact]
    public void SixDigitHex_ParsesRgb()
    {
        var c = HtmlColorParser.FromHtml("#FF8800");
        Assert.Equal(255, c.R);
        Assert.Equal(136, c.G);
        Assert.Equal(0, c.B);
    }

    [Fact]
    public void ThreeDigitHex_ExpandsNibbles()
    {
        var c = HtmlColorParser.FromHtml("#F80");
        Assert.Equal(255, c.R);   // 0xF -> 0xFF
        Assert.Equal(136, c.G);   // 0x8 -> 0x88
        Assert.Equal(0, c.B);
    }

    [Fact]
    public void EightDigitHex_ParsesArgb()
    {
        var c = HtmlColorParser.FromHtml("#80FF8800");
        Assert.Equal(128, c.A);
        Assert.Equal(255, c.R);
        Assert.Equal(136, c.G);
        Assert.Equal(0, c.B);
    }

    [Fact]
    public void NamedColour_Resolves()
    {
        var c = HtmlColorParser.FromHtml("Red");
        Assert.Equal(Color.Red.R, c.R);
        Assert.Equal(Color.Red.G, c.G);
        Assert.Equal(Color.Red.B, c.B);
    }

    [Fact]
    public void EmptyString_ReturnsEmpty()
    {
        // net10's ColorTranslator.FromHtml("") returns Color.Empty; the netstandard branch
        // explicitly returns Color.Empty for null/empty. Pin both to the same result.
        Assert.Equal(Color.Empty, HtmlColorParser.FromHtml(string.Empty));
    }
}
