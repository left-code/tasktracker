using TaskTracker.Models;

namespace TaskTracker.Tests;

public sealed class DayLogStoreTests : IDisposable
{
    private static readonly DateOnly Day = new(2026, 10, 24);
    private static readonly TimeSpan Offset = TimeSpan.FromHours(2);
    private readonly TempStore _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Load_MissingFile_ReturnsEmptyLog()
    {
        var log = _temp.Store.Load(Day);

        Assert.Equal(Day, log.Date);
        Assert.Empty(log.Tasks);
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsAndFillsDurations()
    {
        var log = new DayLog { Date = Day };
        log.GetOrAddTask("Fix login bug").Intervals.Add(Interval(9, 0, 9, 47));
        log.GetOrAddTask("fix login bug ").Intervals.Add(Interval(13, 0, 13, 30));

        _temp.Store.Save(log);
        var loaded = _temp.Store.Load(Day);

        var task = Assert.Single(loaded.Tasks);
        Assert.Equal("Fix login bug", task.Task);
        Assert.Equal(2, task.Intervals.Count);
        Assert.Equal(47 * 60, task.Intervals[0].DurationSeconds);
        Assert.Equal((47 + 30) * 60, task.TotalSeconds);
        Assert.Equal(new DateTimeOffset(2026, 10, 24, 9, 0, 0, Offset), task.Intervals[0].Start);
    }

    [Fact]
    public void Save_WritesExpectedJsonShape()
    {
        var log = new DayLog { Date = Day };
        log.GetOrAddTask("A").Intervals.Add(Interval(9, 0, 9, 10));
        _temp.Store.Save(log);

        var json = File.ReadAllText(_temp.Store.PathFor(Day));

        Assert.Contains("\"date\": \"2026-10-24\"", json);
        Assert.Contains("\"task\": \"A\"", json);
        Assert.Contains("\"totalSeconds\": 600", json);
        Assert.Contains("\"start\": \"2026-10-24T09:00:00+02:00\"", json);
        Assert.DoesNotContain("\"duration\":", json);
        Assert.False(File.Exists(_temp.Store.PathFor(Day) + ".tmp"));
    }

    [Fact]
    public void Load_InvalidJson_Throws()
    {
        System.IO.Directory.CreateDirectory(_temp.Directory);
        File.WriteAllText(_temp.Store.PathFor(Day), "{ not json");

        Assert.Throws<InvalidDataException>(() => _temp.Store.Load(Day));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{ \"date\": \"2026-10-23\", \"tasks\": null }")]
    [InlineData("{ \"date\": \"2026-10-25\", \"tasks\": [ { \"task\": \"A\", \"intervals\": null } ] }")]
    public void Load_UsesFileNameDateAndFillsMissingLists(string json)
    {
        System.IO.Directory.CreateDirectory(_temp.Directory);
        File.WriteAllText(_temp.Store.PathFor(Day), json);

        var log = _temp.Store.Load(Day);

        Assert.Equal(Day, log.Date);
        Assert.NotNull(log.Tasks);
        Assert.All(log.Tasks, t => Assert.NotNull(t.Intervals));
    }

    [Fact]
    public void RecentTaskNames_MostRecentFirst_DistinctAcrossDays()
    {
        var yesterday = new DayLog { Date = Day.AddDays(-1) };
        yesterday.GetOrAddTask("Old").Intervals.Add(Interval(9, 0, 10, 0, Day.AddDays(-1)));
        yesterday.GetOrAddTask("REVIEW").Intervals.Add(Interval(10, 0, 11, 0, Day.AddDays(-1)));
        _temp.Store.Save(yesterday);

        var today = new DayLog { Date = Day };
        today.GetOrAddTask("Review").Intervals.Add(Interval(9, 0, 10, 0));
        today.GetOrAddTask("Write docs").Intervals.Add(Interval(10, 0, 11, 0));
        _temp.Store.Save(today);

        var names = _temp.Store.RecentTaskNames(Day, days: 7);

        Assert.Equal(["Write docs", "Review", "Old"], names);
    }

    private static Interval Interval(int h1, int m1, int h2, int m2, DateOnly? date = null)
    {
        var d = date ?? Day;
        return new Interval
        {
            Start = new DateTimeOffset(d.Year, d.Month, d.Day, h1, m1, 0, Offset),
            End = new DateTimeOffset(d.Year, d.Month, d.Day, h2, m2, 0, Offset),
        };
    }
}
