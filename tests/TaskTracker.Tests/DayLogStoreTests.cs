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

    [Fact]
    public void KnownSpelling_UsesMostRecentDayBeforeTheGivenDate()
    {
        var older = new DayLog { Date = Day.AddDays(-30) };
        older.GetOrAddTask("task-1123").Intervals.Add(Interval(9, 0, 10, 0, Day.AddDays(-30)));
        _temp.Store.Save(older);
        var recent = new DayLog { Date = Day.AddDays(-2) };
        recent.GetOrAddTask("TASK-1123").Intervals.Add(Interval(9, 0, 10, 0, Day.AddDays(-2)));
        _temp.Store.Save(recent);
        var today = new DayLog { Date = Day };
        today.GetOrAddTask("Task-1123").Intervals.Add(Interval(9, 0, 10, 0));
        _temp.Store.Save(today);

        Assert.Equal("TASK-1123", _temp.Store.KnownSpelling(" task-1123 ", before: Day));
        Assert.Equal("Task-1123", _temp.Store.KnownSpelling("task-1123", before: Day.AddDays(1)));
        Assert.Null(_temp.Store.KnownSpelling("task-11", before: Day.AddDays(1)));
    }

    [Fact]
    public void KnownSpelling_SkipsBrokenFilesAndOtherFiles()
    {
        var old = new DayLog { Date = Day.AddDays(-5) };
        old.GetOrAddTask("Review").Intervals.Add(Interval(9, 0, 10, 0, Day.AddDays(-5)));
        _temp.Store.Save(old);
        File.WriteAllText(_temp.Store.PathFor(Day.AddDays(-1)), "{ broken");
        File.WriteAllText(Path.Combine(_temp.Directory, "notes.json"), "{}");

        Assert.Equal("Review", _temp.Store.KnownSpelling("REVIEW", before: Day));
    }

    [Fact]
    public void KnownSpelling_SkipsLockedFiles_AndReadsThemNextTime()
    {
        var old = new DayLog { Date = Day.AddDays(-5) };
        old.GetOrAddTask("Review").Intervals.Add(Interval(9, 0, 10, 0, Day.AddDays(-5)));
        _temp.Store.Save(old);
        var locked = new DayLog { Date = Day.AddDays(-1) };
        locked.GetOrAddTask("REVIEW").Intervals.Add(Interval(9, 0, 10, 0, Day.AddDays(-1)));
        _temp.Store.Save(locked);

        using (File.Open(_temp.Store.PathFor(Day.AddDays(-1)), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Equal("Review", _temp.Store.KnownSpelling("review", before: Day));
            Assert.Equal(["Review"], _temp.Store.RecentTaskNames(Day, days: 7));
        }

        Assert.Equal("REVIEW", _temp.Store.KnownSpelling("review", before: Day));
    }

    [Fact]
    public void KnownSpelling_FindsDaysLoggedAfterTheFirstLookup()
    {
        Assert.Null(_temp.Store.KnownSpelling("Review", before: Day));

        var today = new DayLog { Date = Day };
        today.GetOrAddTask("REVIEW").Intervals.Add(Interval(9, 0, 10, 0));
        _temp.Store.Save(today);

        Assert.Null(_temp.Store.KnownSpelling("Review", before: Day));
        Assert.Equal("REVIEW", _temp.Store.KnownSpelling("Review", before: Day.AddDays(1)));
        Assert.Null(_temp.Store.KnownSpelling("Review", before: Day)); // clock set back
    }

    [Fact]
    public void NameFor_PrefersTodaysSpelling_ThenEarlierDays_ThenTheTypedName()
    {
        var older = new DayLog { Date = Day.AddDays(-3) };
        older.GetOrAddTask("TASK-1").Intervals.Add(Interval(9, 0, 10, 0, Day.AddDays(-3)));
        older.GetOrAddTask("TASK-2").Intervals.Add(Interval(10, 0, 11, 0, Day.AddDays(-3)));
        _temp.Store.Save(older);
        var today = new DayLog { Date = Day };
        today.GetOrAddTask("Task-1").Intervals.Add(Interval(9, 0, 10, 0));
        _temp.Store.Save(today);

        Assert.Equal("Task-1", _temp.Store.NameFor("task-1", Day));
        Assert.Equal("TASK-2", _temp.Store.NameFor("task-2", Day));
        Assert.Equal("new One", _temp.Store.NameFor(" new One ", Day));
    }

    [Fact]
    public void ReadJson_ReturnsTheLogAsLoaded()
    {
        System.IO.Directory.CreateDirectory(_temp.Directory);
        File.WriteAllText(_temp.Store.PathFor(Day), """
            { "date": "2026-01-01", "tasks": [
              { "task": "A", "totalSeconds": 1, "intervals": [
                { "start": "2026-10-24T09:00:00+02:00", "end": "2026-10-24T09:10:00+02:00", "durationSeconds": 1 } ] },
              { "task": "B", "intervals": null } ] }
            """);

        var json = _temp.Store.ReadJson(Day);

        Assert.Contains("\"date\": \"2026-10-24\"", json);
        Assert.Contains("\"totalSeconds\": 600", json);
        Assert.Contains("\"durationSeconds\": 600", json);
        Assert.Contains("\"intervals\": []", json);
    }

    [Fact]
    public void ReadJson_BrokenFile_Throws()
    {
        System.IO.Directory.CreateDirectory(_temp.Directory);
        File.WriteAllText(_temp.Store.PathFor(Day), "{ broken");

        Assert.Throws<InvalidDataException>(() => _temp.Store.ReadJson(Day));
    }

    [Fact]
    public void ReadJson_MissingDay_ReturnsAnEmptyLogInTheSameShape()
    {
        var json = _temp.Store.ReadJson(Day);

        Assert.Contains("\"date\": \"2026-10-24\"", json);
        Assert.Contains("\"tasks\": []", json);
        Assert.False(File.Exists(_temp.Store.PathFor(Day)));
    }

    [Fact]
    public void KnownSpelling_NoLogDirectory_ReturnsNull()
    {
        Assert.Null(_temp.Store.KnownSpelling("A", before: Day));
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
