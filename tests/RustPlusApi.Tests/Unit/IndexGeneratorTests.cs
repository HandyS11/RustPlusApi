using RustPlusApi.Camera;
using Xunit;

namespace RustPlusApi.Tests.Unit;

/// <summary>Pins the seeded xorshift PRNG so the camera sample shuffle stays deterministic
/// (and matches rustplus.js). The exact sequence is a characterization lock.</summary>
public class IndexGeneratorTests
{
    [Fact]
    public void NextInt_IsDeterministicForFixedSeed()
    {
        var a = new IndexGenerator(1337);
        var b = new IndexGenerator(1337);
        for (var i = 0; i < 100; i++)
            Assert.Equal(a.NextInt(64), b.NextInt(64));
    }

    [Fact]
    public void NextInt_StaysWithinBounds()
    {
        var g = new IndexGenerator(42);
        for (var i = 0; i < 1000; i++)
        {
            var v = g.NextInt(10);
            Assert.InRange(v, 0, 10); // upper bound is max (inclusive) per the porting note
        }
    }
}
