using Microsoft.Extensions.Time.Testing;
using TaskTracker.Tracking;

namespace TaskTracker.Tests;

public sealed class TrackerTests : IDisposable
{
    private static readonly DateOnly Day = new(2026, 10, 24);
    private readonly TempStore _temp = new();
    private readonly FakeTimeProvider _time;
    private readonly Tracker _tracker;

    public TrackerTests()
    {
        _time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 24, 9, 0, 0, TimeSpan.Zero));
        _time.SetLocalTimeZone(TimeZoneInfo.Utc);
        _tracker = new Tracker(_temp.Store, _time);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Start_PersistsOpenIntervalImmediately()
    {
        _tracker.Start("A");

        var task = Assert.Single(_temp.Store.Load(Day).Tasks);
        Assert.Equal("A", task.Task);
        Assert.Single(task.Intervals);
        Assert.Equal(TrackerState.Running, _tracker.State);
    }

    [Fact]
    public void Tick_ExtendsOpenIntervalOnDisk()
    {
        _tracker.Start("A");
        Work(minutes: 10);

        var interval = Assert.Single(Assert.Single(_temp.Store.Load(Day).Tasks).Intervals);
        Assert.Equal(TimeSpan.FromMinutes(10), interval.Duration);
    }

    [Fact]
    public void Switch_ClosesCurrentAndOpensNew()
    {
        _tracker.Start("A");
        Work(minutes: 10);
        _tracker.Start("B");
        Work(minutes: 5);
        _tracker.Start("a");
        Work(minutes: 3);
        _tracker.Stop();

        var day = _tracker.GetStatus().Day;
        Assert.Equal(2, day.TaskCount);
        Assert.Equal(TimeSpan.FromMinutes(13), day.Find("A")!.Total);
        Assert.Equal(2, day.Find("A")!.IntervalCount);
        Assert.Equal(TimeSpan.FromMinutes(5), day.Find("B")!.Total);
        Assert.Equal(TimeSpan.FromMinutes(18), day.Total);
    }

    [Fact]
    public void Pause_DoesNotCountTimeUntilResume()
    {
        _tracker.Start("A");
        Work(minutes: 10);
        _tracker.Pause();
        Work(minutes: 30);
        Assert.Equal(TrackerState.Paused, _tracker.State);
        _tracker.Resume();
        Work(minutes: 5);
        _tracker.Stop();

        var task = _tracker.GetStatus().Day.Find("A")!;
        Assert.Equal(TimeSpan.FromMinutes(15), task.Total);
        Assert.Equal(2, task.IntervalCount);
        Assert.Equal(TrackerState.Idle, _tracker.State);
    }

    [Fact]
    public void Status_ReportsCurrentTaskTotalIncludingOpenInterval()
    {
        _tracker.Start("A");
        Work(minutes: 2);
        _time.Advance(TimeSpan.FromSeconds(30)); // between heartbeats

        var status = _tracker.GetStatus();
        Assert.Equal("A", status.CurrentTask);
        Assert.Equal(TimeSpan.FromSeconds(150), status.CurrentTaskTotal);
    }

    [Fact]
    public void Midnight_SplitsIntervalAcrossDayFiles()
    {
        _time.SetUtcNow(new DateTimeOffset(2026, 10, 24, 23, 50, 0, TimeSpan.Zero));
        _tracker.Start("Late");
        Work(minutes: 20);
        _tracker.Stop();

        var before = Assert.Single(Assert.Single(_temp.Store.Load(Day).Tasks).Intervals);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.Zero), before.End);
        Assert.Equal(TimeSpan.FromMinutes(10), before.Duration);

        var after = Assert.Single(Assert.Single(_temp.Store.Load(Day.AddDays(1)).Tasks).Intervals);
        Assert.Equal(before.End, after.Start);
        Assert.Equal(TimeSpan.FromMinutes(10), after.Duration);
        Assert.Equal(Day.AddDays(1), _tracker.Today);
    }

    [Fact]
    public void SleepGap_IsNotCountedAsWork()
    {
        _tracker.Start("A");
        Work(minutes: 10);
        _time.Advance(TimeSpan.FromHours(2)); // PC asleep: no heartbeats
        _tracker.Tick();
        Work(minutes: 5);
        _tracker.Stop();

        var task = _tracker.GetStatus().Day.Find("A")!;
        Assert.Equal(TimeSpan.FromMinutes(15), task.Total);
        Assert.Equal(2, task.IntervalCount);
    }

    [Fact]
    public void NewTracker_SeesEarlierSessionsOfTheDay()
    {
        _tracker.Start("A");
        Work(minutes: 10);
        _tracker.Stop();

        var restarted = new Tracker(_temp.Store, _time);
        restarted.Start("B");
        Work(minutes: 5, restarted);

        var day = restarted.GetStatus().Day;
        Assert.Equal(2, day.TaskCount);
        Assert.Equal(TimeSpan.FromMinutes(15), day.Total);
    }

    [Fact]
    public void Start_SameRunningTask_DoesNotSplitInterval()
    {
        _tracker.Start("A");
        Work(minutes: 5);
        _tracker.Start(" a ");
        Work(minutes: 5);

        var task = _tracker.GetStatus().Day.Find("A")!;
        Assert.Equal(1, task.IntervalCount);
        Assert.Equal(TimeSpan.FromMinutes(10), task.Total);
    }

    [Fact]
    public void SwitchRightAfterWake_LeavesNoZeroLengthInterval()
    {
        _tracker.Start("A");
        Work(minutes: 10);
        _time.Advance(TimeSpan.FromHours(1)); // asleep
        _tracker.Start("B");                  // first thing after waking
        Work(minutes: 5);

        var day = _tracker.GetStatus().Day;
        Assert.Equal(1, day.Find("A")!.IntervalCount);
        Assert.Equal(TimeSpan.FromMinutes(10), day.Find("A")!.Total);
        Assert.DoesNotContain(day.Intervals, i => i.Duration == TimeSpan.Zero);
    }

    [Fact]
    public void ClockSetBack_DoesNotProduceNegativeDurations()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 10, 24, 9, 0, 0, TimeSpan.Zero));
        var tracker = new Tracker(_temp.Store, clock);
        tracker.Start("A");
        clock.Work(tracker, minutes: 10);
        clock.Now -= TimeSpan.FromHours(1);
        tracker.Tick(); // the app notices within 200 ms (UI poll); time before that is not counted
        clock.Work(tracker, minutes: 5);

        var day = tracker.GetStatus().Day;
        Assert.All(day.Intervals, i => Assert.True(i.Duration >= TimeSpan.Zero));
        Assert.Equal(TimeSpan.FromMinutes(15), day.Total);
    }

    [Fact]
    public void SmallClockCorrection_DoesNotSplitInterval()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 10, 24, 9, 0, 0, TimeSpan.Zero));
        var tracker = new Tracker(_temp.Store, clock);
        tracker.Start("A");
        clock.Work(tracker, minutes: 2);
        clock.Now -= TimeSpan.FromSeconds(2);
        clock.Work(tracker, minutes: 2);

        Assert.Equal(1, tracker.GetStatus().Day.Find("A")!.IntervalCount);
    }

    /// <summary>A clock that, unlike FakeTimeProvider, can be set back (like a user or NTP adjusting it).</summary>
    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public void Work(Tracker tracker, int minutes)
        {
            for (var i = 0; i < minutes; i++)
            {
                Now += TimeSpan.FromMinutes(1);
                tracker.Tick();
            }
        }
    }

    [Fact]
    public void PausedOverSeveralDays_WritesNoEmptyDayFiles()
    {
        _tracker.Start("A");
        Work(minutes: 10);
        _tracker.Pause();
        _time.Advance(TimeSpan.FromDays(3));
        _tracker.Tick();

        Assert.Equal(Day.AddDays(3), _tracker.Today);
        Assert.True(File.Exists(_temp.Store.PathFor(Day)));
        Assert.False(File.Exists(_temp.Store.PathFor(Day.AddDays(1))));
        Assert.False(File.Exists(_temp.Store.PathFor(Day.AddDays(2))));
    }

    [Fact]
    public void FailedSaveOnPause_IsWrittenByNextTick()
    {
        _tracker.Start("A");
        Work(minutes: 10);

        using (new FileStream(_temp.Store.PathFor(Day), FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.ThrowsAny<Exception>(_tracker.Pause);

        Assert.Equal(TrackerState.Paused, _tracker.State);
        _time.Advance(TimeSpan.FromMinutes(1));
        _tracker.Tick();

        var interval = Assert.Single(Assert.Single(_temp.Store.Load(Day).Tasks).Intervals);
        Assert.Equal(TimeSpan.FromMinutes(10), interval.Duration);
    }

    [Fact]
    public void Start_TaskFromAnEarlierDay_KeepsItsSpelling()
    {
        _tracker.Start("TASK-1123");
        Work(minutes: 5);
        _tracker.Stop();
        _time.Advance(TimeSpan.FromDays(3));

        var later = new Tracker(_temp.Store, _time);
        later.Start("task-1123 ");
        Work(minutes: 1, later);
        later.Start("new Task");

        Assert.Equal("new Task", later.CurrentTask);
        var names = _temp.Store.Load(Day.AddDays(3)).Tasks.Select(t => t.Task);
        Assert.Equal(["TASK-1123", "new Task"], names);
    }

    [Fact]
    public void Start_WhileAnOldLogIsLocked_StillStarts()
    {
        var yesterday = new DateTimeOffset(2026, 10, 23, 9, 0, 0, TimeSpan.Zero);
        var old = new Models.DayLog { Date = Day.AddDays(-1) };
        old.GetOrAddTask("Review").Intervals.Add(new Models.Interval { Start = yesterday, End = yesterday.AddHours(1) });
        _temp.Store.Save(old);

        using (File.Open(_temp.Store.PathFor(Day.AddDays(-1)), FileMode.Open, FileAccess.Read, FileShare.None))
            _tracker.Start("review");

        Assert.Equal(TrackerState.Running, _tracker.State);
        Assert.Equal("review", _tracker.CurrentTask);
    }

    /// <summary>Advances the clock minute by minute with a heartbeat, like the real app.</summary>
    private void Work(int minutes, Tracker? tracker = null)
    {
        for (var i = 0; i < minutes; i++)
        {
            _time.Advance(TimeSpan.FromMinutes(1));
            (tracker ?? _tracker).Tick();
        }
    }
}
