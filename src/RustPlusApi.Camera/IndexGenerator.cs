namespace RustPlusApi.Camera;

/// <summary>
/// Seeded xorshift PRNG used to shuffle the camera sample-position buffer. Ported
/// faithfully from liamcottle/rustplus.js so the shuffle matches the server's layout.
/// </summary>
internal sealed class IndexGenerator
{
    private int _state;

    public IndexGenerator(int seed)
    {
        _state = seed;
        NextState();
    }

    public int NextInt(int max)
    {
        var t = (int)((NextState() * (long)max) / 4294967295L);
        if (t < 0) t = max + t - 1;
        return t;
    }

    public long NextState()
    {
        unchecked
        {
            var e = _state;
            var t = e;
            e ^= e << 13;
            e ^= (int)((uint)e >> 17);
            e ^= e << 5;
            _state = e;
            return t >= 0 ? t : 4294967295L + t - 1;
        }
    }
}
