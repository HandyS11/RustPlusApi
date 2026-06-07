namespace RustPlusApi.Data;

/// <summary>In-game time and day/night cycle parameters returned by <c>GetTimeAsync</c>.</summary>
public sealed record TimeInfo
{
    /// <summary>Real-time length of a full in-game day, in minutes.</summary>
    public float DayLengthMinutes { get; init; }

    /// <summary>Multiplier that controls how fast in-game time advances relative to real time.</summary>
    public float TimeScale { get; init; }

    /// <summary>In-game hour at which the sun rises (0–24 scale).</summary>
    public float Sunrise { get; init; }

    /// <summary>In-game hour at which the sun sets (0–24 scale).</summary>
    public float Sunset { get; init; }

    /// <summary>Current in-game time of day (0–24 scale).</summary>
    public float Time { get; init; }
}
