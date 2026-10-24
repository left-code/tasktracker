namespace TaskTracker.Tracking;

/// <summary>Local-time helpers shared by the interactive tracker and the start/end commands.</summary>
public static class LocalClock
{
    // Whole seconds keep the JSON timestamps short and readable.
    public static DateTimeOffset Now(TimeProvider time) => Truncate(time.GetLocalNow());

    public static DateTimeOffset Truncate(DateTimeOffset value) =>
        value.AddTicks(-(value.Ticks % TimeSpan.TicksPerSecond));

    /// <summary>The calendar date of a local timestamp (its own offset decides the day).</summary>
    public static DateOnly DateOf(DateTimeOffset time) => DateOnly.FromDateTime(time.DateTime);

    public static DateTimeOffset MidnightOf(DateOnly date, TimeZoneInfo zone)
    {
        var midnight = date.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(midnight, zone.GetUtcOffset(midnight));
    }
}
