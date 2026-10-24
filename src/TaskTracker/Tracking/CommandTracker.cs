using TaskTracker.Models;
using TaskTracker.Storage;

namespace TaskTracker.Tracking;

/// <param name="Worked">Time written to the logs.</param>
/// <param name="Off">Time the PC was asleep or off, left out of the logs.</param>
/// <param name="OffUnknown">The power history couldn't be read, so nothing was left out.</param>
public sealed record EndedTask(string Task, DateTimeOffset Start, DateTimeOffset End, TimeSpan Worked, TimeSpan Off, bool OffUnknown);

/// <summary>Result of <see cref="CommandTracker.Start"/>.</summary>
/// <param name="Ended">The task that was running before and was ended by this start, if any.</param>
/// <param name="AlreadyRunning">The task was already running; nothing changed.</param>
public sealed record StartedTask(OpenTask Open, EndedTask? Ended, bool AlreadyRunning);

/// <summary>
/// Backs <c>TaskTracker start</c> and <c>end</c>: no process runs in between, so the start is kept in
/// <see cref="OpenTaskStore"/> and the interval is written to the day logs when the task ends.
/// Callers hold the single-instance mutex, so nothing else writes the logs meanwhile.
/// </summary>
public sealed class CommandTracker(DayLogStore logs, OpenTaskStore openTask, IPowerHistory power, TimeProvider time)
{
    /// <summary>
    /// One interval longer than this is almost certainly a forgotten <c>end</c> or a broken start
    /// time; recording it would fill a day file for every day in between.
    /// </summary>
    public static readonly TimeSpan MaxOpen = TimeSpan.FromDays(7);

    /// <summary>Small backward clock corrections (NTP) are absorbed, as in <see cref="Tracker"/>.</summary>
    private static readonly TimeSpan MaxBackstep = TimeSpan.FromSeconds(5);

    public OpenTask? Current => openTask.Load();

    /// <summary>Starts a task, ending the running one first (this is also "switch").</summary>
    public StartedTask Start(string task)
    {
        var now = LocalClock.Now(time);
        var current = openTask.Load();
        if (current is not null && current.Is(task))
            return new StartedTask(current, null, AlreadyRunning: true);

        // Looked up before anything is written, so if it fails nothing has changed.
        var name = logs.NameFor(task, LocalClock.DateOf(now));
        var ended = current is null ? null : Record(current, now);
        var started = new OpenTask { Task = name, Start = now };
        openTask.Save(started);
        return new StartedTask(started, ended, AlreadyRunning: false);
    }

    /// <summary>Ends the running task and writes its time to the logs. Returns null if nothing is running.</summary>
    public EndedTask? End()
    {
        var current = openTask.Load();
        if (current is null)
            return null;

        var ended = Record(current, LocalClock.Now(time));
        openTask.Delete();
        return ended;
    }

    /// <summary>
    /// Writes start..end to the day logs, minus the time the PC was off and split at local midnight.
    /// Throws, keeping the open task, when the clock is behind the start or the task ran too long.
    /// </summary>
    /// <remarks>
    /// Safe to repeat after a crash before the open task is cleared: this task's intervals inside
    /// start..end are replaced, not added to. Nothing else writes them, because the app picks up an
    /// open task before it tracks anything.
    /// </remarks>
    private EndedTask Record(OpenTask task, DateTimeOffset end)
    {
        var start = task.Start;
        if (end < start)
        {
            if (start - end > MaxBackstep)
                throw openTask.Broken($"says {task.Task} started at {start:yyyy-MM-dd HH:mm}, after the current time ({end:yyyy-MM-dd HH:mm}). Check the clock.");
            end = start;
        }
        if (end - start > MaxOpen)
            throw openTask.Broken($"says {task.Task} started at {start:yyyy-MM-dd HH:mm}, more than {MaxOpen.TotalDays:0} days ago. Set the start to when you really began.");

        var off = end > start ? power.OffPeriods(start, end) : [];
        var pieces = Subtract(start, end, off ?? []).SelectMany(SplitAtMidnight).ToList();

        var worked = TimeSpan.Zero;
        for (var date = LocalClock.DateOf(start); date <= LocalClock.DateOf(end); date = date.AddDays(1))
        {
            var day = pieces.Where(p => LocalClock.DateOf(p.Start) == date).ToList();
            var log = logs.Load(date);
            var existing = log.FindTask(task.Task);
            var removed = existing?.Intervals.RemoveAll(i => i.Start >= start && i.End <= end) ?? 0;
            if (day.Count == 0 && removed == 0)
                continue;

            var taskLog = existing ?? log.GetOrAddTask(task.Task);
            foreach (var piece in day)
            {
                taskLog.Intervals.Add(new Interval { Start = piece.Start, End = piece.End });
                worked += piece.End - piece.Start;
            }
            taskLog.Intervals.Sort((a, b) => a.Start.CompareTo(b.Start));
            if (taskLog.Intervals.Count == 0)
                log.Tasks.Remove(taskLog);
            logs.Save(log);
        }

        return new EndedTask(task.Task, start, end, worked, end - start - worked, OffUnknown: off is null);
    }

    /// <summary>start..end with the off periods cut out, in local time and whole seconds; empty pieces dropped.</summary>
    private List<TimeRange> Subtract(DateTimeOffset start, DateTimeOffset end, IReadOnlyList<TimeRange> off)
    {
        var pieces = new List<TimeRange>();
        var cursor = start;
        foreach (var gap in off.Select(Local).Where(g => g.End > g.Start).OrderBy(g => g.Start))
        {
            if (gap.End <= cursor || gap.Start >= end)
                continue;
            if (gap.Start > cursor)
                pieces.Add(new TimeRange(cursor, gap.Start));
            if (gap.End > cursor)
                cursor = gap.End;
        }
        if (end > cursor)
            pieces.Add(new TimeRange(cursor, end));
        return pieces;
    }

    private TimeRange Local(TimeRange range) => new(
        LocalClock.Truncate(TimeZoneInfo.ConvertTime(range.Start, time.LocalTimeZone)),
        LocalClock.Truncate(TimeZoneInfo.ConvertTime(range.End, time.LocalTimeZone)));

    private IEnumerable<TimeRange> SplitAtMidnight(TimeRange range)
    {
        var start = range.Start;
        while (LocalClock.DateOf(range.End) > LocalClock.DateOf(start))
        {
            var midnight = LocalClock.MidnightOf(LocalClock.DateOf(start).AddDays(1), time.LocalTimeZone);
            yield return new TimeRange(start, midnight);
            start = midnight;
        }
        if (range.End > start)
            yield return new TimeRange(start, range.End);
    }
}
