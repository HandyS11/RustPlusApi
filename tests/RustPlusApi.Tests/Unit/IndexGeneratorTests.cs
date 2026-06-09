using RustPlusApi.Camera;
using Xunit;

namespace RustPlusApi.Tests.Unit;

/// <summary>
/// Pins the seeded xorshift PRNG so the camera sample shuffle stays deterministic
/// (and matches rustplus.js). Exact sequences are characterization locks — any
/// arithmetic mutation (shift constants 13/17/5, divisor 4294967295L, xor operators)
/// produces a different series.
/// </summary>
public class IndexGeneratorTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // Exact sequence pins (kills arithmetic / constant mutations)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NextInt_Seed1337_ExactSequence()
    {
        // Captured from the real implementation; any xorshift mutation diverges.
        var g = new IndexGenerator(1337);
        Assert.Equal(5,  g.NextInt(64));
        Assert.Equal(51, g.NextInt(64));
        Assert.Equal(57, g.NextInt(64));
        Assert.Equal(14, g.NextInt(64));
        Assert.Equal(57, g.NextInt(64));
        Assert.Equal(33, g.NextInt(64));
        Assert.Equal(21, g.NextInt(64));
        Assert.Equal(18, g.NextInt(64));
        Assert.Equal(58, g.NextInt(64));
        Assert.Equal(29, g.NextInt(64));
        Assert.Equal(57, g.NextInt(64));
        Assert.Equal(63, g.NextInt(64));
    }

    [Fact]
    public void NextInt_Seed42_ExactSequence()
    {
        var g = new IndexGenerator(42);
        Assert.Equal(0, g.NextInt(10));
        Assert.Equal(6, g.NextInt(10));
        Assert.Equal(1, g.NextInt(10));
        Assert.Equal(8, g.NextInt(10));
        Assert.Equal(8, g.NextInt(10));
        Assert.Equal(3, g.NextInt(10));
        Assert.Equal(8, g.NextInt(10));
        Assert.Equal(5, g.NextInt(10));
        Assert.Equal(7, g.NextInt(10));
        Assert.Equal(2, g.NextInt(10));
        Assert.Equal(6, g.NextInt(10));
        Assert.Equal(7, g.NextInt(10));
    }

    [Fact]
    public void NextInt_Seed999_ExactSequence()
    {
        var g = new IndexGenerator(999);
        Assert.Equal(15,  g.NextInt(256));
        Assert.Equal(131, g.NextInt(256));
        Assert.Equal(87,  g.NextInt(256));
        Assert.Equal(69,  g.NextInt(256));
        Assert.Equal(239, g.NextInt(256));
        Assert.Equal(12,  g.NextInt(256));
        Assert.Equal(77,  g.NextInt(256));
        Assert.Equal(212, g.NextInt(256));
        Assert.Equal(130, g.NextInt(256));
        Assert.Equal(222, g.NextInt(256));
        Assert.Equal(113, g.NextInt(256));
        Assert.Equal(125, g.NextInt(256));
    }

    [Fact]
    public void NextInt_Seed1337_LargeDomain_ExactSequence()
    {
        var g = new IndexGenerator(1337);
        Assert.Equal(79,  g.NextInt(1000));
        Assert.Equal(803, g.NextInt(1000));
        Assert.Equal(896, g.NextInt(1000));
        Assert.Equal(227, g.NextInt(1000));
        Assert.Equal(903, g.NextInt(1000));
        Assert.Equal(530, g.NextInt(1000));
        Assert.Equal(330, g.NextInt(1000));
        Assert.Equal(287, g.NextInt(1000));
        Assert.Equal(918, g.NextInt(1000));
        Assert.Equal(459, g.NextInt(1000));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Determinism (two generators from the same seed must stay in lock-step)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NextInt_IsDeterministicForFixedSeed()
    {
        var a = new IndexGenerator(1337);
        var b = new IndexGenerator(1337);
        for (var i = 0; i < 100; i++)
            Assert.Equal(a.NextInt(64), b.NextInt(64));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Bounds check (kills off-by-one mutations on the result formula)
    // ──────────────────────────────────────────────────────────────────────────

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

    [Fact]
    public void NextInt_MaxOne_AlwaysReturnsZero()
    {
        // When max==1, result is always 0 (NextState()*1/4294967295 truncates to 0).
        var g = new IndexGenerator(1337);
        for (var i = 0; i < 20; i++)
            Assert.Equal(0, g.NextInt(1));
    }

    [Fact]
    public void NextInt_MaxTwo_ExactBinarySequence()
    {
        // Captures the exact 0/1 stream for seed 1337, killing the divisor mutation.
        var g = new IndexGenerator(1337);
        foreach (var exp in new int[] { 0, 1, 1, 0, 1, 1, 0, 0, 1, 0 })
        {
            Assert.Equal(exp, g.NextInt(2));
        }
    }

    [Fact]
    public void NextInt_DifferentSeeds_ProduceDifferentSequences()
    {
        // Two different seeds should diverge immediately (kills seed-ignored mutation).
        var a = new IndexGenerator(1337);
        var b = new IndexGenerator(1338);
        bool anyDifferent = false;
        for (int i = 0; i < 10; i++)
        {
            if (a.NextInt(1000) != b.NextInt(1000))
            {
                anyDifferent = true;
                break;
            }
        }

        Assert.True(anyDifferent);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // NextState sign-branch: kills #146 (t > 0 vs t >= 0)
    //
    // Seed 0 is a fixed point: every NextState() call returns t=0, _state stays 0.
    // Normal (t >= 0):  branch taken → NextState() = 0 → NextInt(100) = 0.
    // Mutant (t > 0):   t==0 takes else  → NextState() = 4294967295L−1 → NextInt(100) ≈ 99.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NextInt_SeedZero_AlwaysReturnsZero()
    {
        var g = new IndexGenerator(0);
        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(0, g.NextInt(100));
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // NextState negative-t branch: kills #147 (4294967295L + t + 1 vs - 1)
    //
    // Seed 1 produces a negative internal state on the 3rd NextState call
    // (t = −1 647 531 835). The +1/−1 constant creates a difference of 2 in
    // NextState(); with max=Int32.MaxValue that maps to a 1-unit difference in
    // the returned integer.
    //   Normal:  NextInt(Int32.MaxValue)[2] = 1 323 717 729
    //   Mutant:  NextInt(Int32.MaxValue)[2] = 1 323 717 730
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NextInt_Seed1_LargeMax_ExactNegativeStateBranchValue()
    {
        var g = new IndexGenerator(1);
        Assert.Equal(135184,      g.NextInt(int.MaxValue)); // t positive
        Assert.Equal(33817344,    g.NextInt(int.MaxValue)); // t positive
        Assert.Equal(1323717729,  g.NextInt(int.MaxValue)); // t negative → branch exercised
        Assert.Equal(153799847,   g.NextInt(int.MaxValue)); // t positive
        Assert.Equal(1199344615,  g.NextInt(int.MaxValue)); // t negative again
    }
}
