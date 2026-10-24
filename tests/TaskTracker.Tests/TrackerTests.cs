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
