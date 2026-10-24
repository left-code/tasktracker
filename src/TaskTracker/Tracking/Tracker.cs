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
/// <remarks>
/// Every change is made in memory first and then saved. If a save fails, the log stays marked
/// dirty and the next <see cref="Tick"/> writes it, so a failed save never loses state.
/// </remarks>
public sealed class Tracker
{
    /// <summary>
    /// If the clock jumps further than this between two observations (PC asleep or hibernated),
    /// the open interval is ended where we last saw it, so sleep time is not counted as work.
    /// </summary>
    public static readonly TimeSpan MaxGap = TimeSpan.FromMinutes(5);

    /// <summary>Small backward clock corrections (NTP) are absorbed instead of splitting the interval.</summary>
    private static readonly TimeSpan MaxBackstep = TimeSpan.FromSeconds(5);

    private readonly Lock _lock = new();
    private readonly DayLogStore _store;
    private readonly TimeProvider _time;
    private DayLog _today;
    private Interval? _open;
    private DateTimeOffset _lastSeen;
    private bool _dirty;

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
            if (State == TrackerState.Running && string.Equals(
                    DayLog.NormalizeName(task), CurrentTask, StringComparison.OrdinalIgnoreCase))
                return; // already working on it; don't split the interval

            CloseOpen(now);
            CurrentTask = _today.GetOrAddTask(task).Task;
            Open(now);
            State = TrackerState.Running;
            SaveToday();
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
            SaveToday();
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
            SaveToday();
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            CloseOpen(CatchUp());
            State = TrackerState.Idle;
            CurrentTask = null;
            if (_dirty)
                SaveToday();
        }
    }

    /// <summary>
    /// Stops tracking and reads today's log back from disk, so the report shows exactly what was
    /// saved and nothing keeps ticking while it is on screen.
    /// </summary>
    public DaySummary StopForReport()
    {
        lock (_lock)
        {
            Stop(); // Lock is reentrant
            return DaySummary.From(_store.Load(_today.Date));
        }
    }

    /// <summary>Heartbeat: persists the running interval (and any failed save) so a crash loses at most one beat.</summary>
    public void Tick()
    {
        lock (_lock)
        {
            CatchUp();
            if (_dirty)
                SaveToday();
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
    /// Brings state up to the current time: handles sleep gaps, clock changes and midnight
    /// rollover, and extends the open interval to now. Only the midnight rollover saves (the old
    /// day must be written before it is replaced); everything else is left for the caller.
    /// </summary>
    private DateTimeOffset CatchUp()
    {
        var now = Now();
        if (now < _lastSeen && _lastSeen - now <= MaxBackstep)
            now = _lastSeen;

        // Asleep, or the clock was set back: end the interval where we last saw it.
        // The invariant at the bottom reopens it at now.
        if (_open is not null && (now - _lastSeen > MaxGap || now < _lastSeen))
            CloseOpen(_lastSeen);

        while (DateOf(now) > _today.Date)
        {
            var nextDay = _today.Date.AddDays(1);
            var midnight = MidnightOf(nextDay);
            var wasOpen = _open is not null;
            if (wasOpen)
                CloseOpen(midnight);
            if (_dirty)
                SaveToday(); // if this throws, _today is unchanged and the next call repeats this step

            _today = _store.Load(nextDay);
            if (wasOpen)
                Open(midnight);
        }

        if (State == TrackerState.Running && _open is null)
            Open(now);
        if (_open is not null)
        {
            _open.End = now;
            _dirty = true;
        }

        _lastSeen = now;
        return now;
    }

    private void Open(DateTimeOffset at)
    {
        _open = new Interval { Start = at, End = at };
        _today.GetOrAddTask(CurrentTask!).Intervals.Add(_open);
        _dirty = true;
    }

    private void CloseOpen(DateTimeOffset at)
    {
        if (_open is null)
            return;

        _open.End = at;
        if (_open.End <= _open.Start)
        {
            // Opened and closed in the same second (e.g. reopened after a wake, then switched
            // right away): drop it rather than record a zero-length interval.
            var task = _today.Tasks.First(t => t.Intervals.Contains(_open));
            task.Intervals.Remove(_open);
            if (task.Intervals.Count == 0)
                _today.Tasks.Remove(task);
        }
        _open = null;
        _dirty = true;
    }

    private void SaveToday()
    {
        _dirty = true;
        _store.Save(_today);
        _dirty = false;
    }

    private DateTimeOffset Now() => LocalClock.Now(_time);

    private static DateOnly DateOf(DateTimeOffset time) => LocalClock.DateOf(time);

    private DateTimeOffset MidnightOf(DateOnly date) => LocalClock.MidnightOf(date, _time.LocalTimeZone);
}
