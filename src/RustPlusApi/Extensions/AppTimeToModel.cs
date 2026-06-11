using RustPlusApi.Data;
using RustPlusContracts;

namespace RustPlusApi.Extensions;

/// <summary>Mapping extensions from protobuf time messages to model types.</summary>
public static class AppTimeToModel
{
    /// <summary>Maps an <see cref="AppTime"/> to a <see cref="TimeInfo"/>.</summary>
    /// <param name="appTime">The protobuf time response.</param>
    public static TimeInfo ToTimeInfo(this AppTime appTime)
    {
        return new TimeInfo
        {
            DayLengthMinutes = appTime.DayLengthMinutes,
            TimeScale = appTime.TimeScale,
            Sunrise = appTime.Sunrise,
            Sunset = appTime.Sunset,
            Time = appTime.Time
        };
    }
}
