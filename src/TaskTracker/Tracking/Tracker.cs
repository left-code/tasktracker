using TaskTracker.Models;
using TaskTracker.Reporting;
using TaskTracker.Storage;

namespace TaskTracker.Tracking;

public enum TrackerState { Idle, Running, Paused }

public sealed record TrackerStatus(TrackerState State, string? CurrentTask, TimeSpan CurrentTaskTotal, DaySummary Day);

/// <summary>
/// Owns the running interval and today's log. Thread-safe: the UI thread drives it, while a
/// background heartbeat calls <see cref="Tick"/> and shutdown handlers call <see cref="Stop"/>.
/// </summary>
public sealed class Tracker
{
    /// <summary>
    /// If the clock jumps further than this between two observations (PC asleep or hibernated),
    /// the open interval is ended where we last saw it, so sleep time is not counted as work.
    /// </summary>
    public static readonly TimeSpan MaxGap = TimeSpan.FromMinutes(5);

    private readonly Lock _lock = new();
    private readonly DayLogStore _store;
    private readonly TimeProvider _time;
    private DayLog _today;
    private Interval? _open;
    private DateTimeOffset _lastSeen;

    public Tracker(DayLogStore store, TimeProvider time)
    {
        _store = store;
        _time = time;
        _lastSeen = Now();
        _today = store.Load(DateOf(_lastSeen));
    }

    public TrackerState State { get; private set; }
    public string? CurrentTask { get; private set; }

    public DateOnly Today
    {
        get { lock (_lock) return _today.Date; }
    }

    /// <summary>Starts working on a task, closing the current interval first (this is also "switch").</summary>
    public void Start(string task)
    {
        lock (_lock)
        {
            var now = CatchUp();
            CloseOpen(now);
            CurrentTask = _today.GetOrAddTask(task).Task;
            Open(now);
            State = TrackerState.Running;
            _store.Save(_today);
        }
    }

    public void Pause()
    {
        lock (_lock)
        {
            if (State != TrackerState.Running)
                return;
            CloseOpen(CatchUp());
            State = TrackerState.Paused;
            _store.Save(_today);
        }
    }

    public void Resume()
    {
        lock (_lock)
        {
            if (State != TrackerState.Paused)
                return;
            Open(CatchUp());
            State = TrackerState.Running;
            _store.Save(_today);
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            var wasOpen = _open is not null;
            CloseOpen(CatchUp());
            State = TrackerState.Idle;
            CurrentTask = null;
            if (wasOpen)
                _store.Save(_today);
        }
    }

    /// <summary>Heartbeat: persists the running interval so a crash loses at most one beat.</summary>
    public void Tick()
    {
        lock (_lock)
        {
            CatchUp();
            if (_open is not null)
                _store.Save(_today);
        }
    }

    public TrackerStatus GetStatus()
    {
        lock (_lock)
        {
            CatchUp();
            var day = DaySummary.From(_today);
            var currentTotal = CurrentTask is null ? TimeSpan.Zero : day.Find(CurrentTask)?.Total ?? TimeSpan.Zero;
            return new TrackerStatus(State, CurrentTask, currentTotal, day);
        }
    }

    /// <summary>
    /// Brings state up to the current time: handles sleep gaps and midnight rollover, and
    /// extends the open interval to now (in memory; callers decide when to save).
    /// </summary>
    private DateTimeOffset CatchUp()
    {
        var now = Now();

        var resumeAfterGap = false;
        if (_open is not null && now - _lastSeen > MaxGap)
        {
            _open.End = _lastSeen;
            _open = null;
            _store.Save(_today);
            resumeAfterGap = true;
        }

        while (DateOf(now) > _today.Date)
        {
            var nextDay = _today.Date.AddDays(1);
            var midnight = MidnightOf(nextDay);
            var wasOpen = _open is not null;
            if (wasOpen)
            {
                _open!.End = midnight;
                _store.Save(_today);
            }
            _today = _store.Load(nextDay);
            if (wasOpen)
                Open(midnight);
        }

        if (resumeAfterGap)
            Open(now);
        if (_open is not null)
            _open.End = now;

        _lastSeen = now;
        return now;
    }

    private void Open(DateTimeOffset at)
    {
        _open = new Interval { Start = at, End = at };
        _today.GetOrAddTask(CurrentTask!).Intervals.Add(_open);
    }

    private void CloseOpen(DateTimeOffset now)
    {
        if (_open is null)
            return;
        _open.End = now;
        _open = null;
    }

    // Whole seconds keep the JSON timestamps short and readable.
    private DateTimeOffset Now()
    {
        var now = _time.GetLocalNow();
        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
    }

    private static DateOnly DateOf(DateTimeOffset time) => DateOnly.FromDateTime(time.DateTime);

    private DateTimeOffset MidnightOf(DateOnly date)
    {
        var midnight = date.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(midnight, _time.LocalTimeZone.GetUtcOffset(midnight));
    }
}
