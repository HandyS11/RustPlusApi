namespace RustPlusApi.Data;

public sealed record TimeInfo
{
    public float DayLengthMinutes { get; init; }
    public float TimeScale { get; init; }
    public float Sunrise { get; init; }
    public float Sunset { get; init; }
    public float Time { get; init; }
}
