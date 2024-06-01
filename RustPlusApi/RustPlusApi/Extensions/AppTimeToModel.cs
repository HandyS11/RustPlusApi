using RustPlusApi.Data;

using RustPlusContracts;

namespace RustPlusApi.Extensions
{
    public static class AppTimeToModel
    {
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
}
