namespace DestNote;

/// <summary>
/// The app's logical day starts at 04:00 instead of 00:00.
/// </summary>
public static class DayClock
{
    public const int StartHour = 4;

    public static DateTime Today => BusinessDayOf(DateTime.Now);

    public static DateTime BusinessDayOf(DateTime timestamp)
    {
        var date = timestamp.Date;
        return timestamp.TimeOfDay < TimeSpan.FromHours(StartHour) ? date.AddDays(-1) : date;
    }

    public static DateTime NextBoundary(DateTime timestamp)
    {
        var boundary = timestamp.Date.AddHours(StartHour);
        if (timestamp >= boundary)
            boundary = boundary.AddDays(1);
        return boundary;
    }

    public static TimeSpan UntilNextBoundary(DateTime timestamp)
        => NextBoundary(timestamp) - timestamp;
}
