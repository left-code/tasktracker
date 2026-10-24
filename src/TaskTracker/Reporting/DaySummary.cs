using TaskTracker.Models;

namespace TaskTracker.Reporting;

public sealed record TaskSummary(
    string Task, int IntervalCount, DateTimeOffset FirstStart, DateTimeOffset LastEnd, TimeSpan Total);

public sealed record IntervalSummary(string Task, DateTimeOffset Start, DateTimeOffset End)
{
    public TimeSpan Duration => End - Start;
}

/// <summary>Immutable snapshot of a day: per-task totals plus every interval in time order.</summary>
public sealed record DaySummary(
    DateOnly Date, IReadOnlyList<TaskSummary> Tasks, IReadOnlyList<IntervalSummary> Intervals, TimeSpan Total)
{
    public int TaskCount => Tasks.Count;

    public TaskSummary? Find(string task) =>
        Tasks.FirstOrDefault(t => string.Equals(t.Task, DayLog.NormalizeName(task), StringComparison.OrdinalIgnoreCase));

    public static DaySummary From(DayLog log)
    {
        var tasks = log.Tasks
            .Where(t => t.Intervals.Count > 0)
            .Select(t => new TaskSummary(
                t.Task,
                t.Intervals.Count,
                t.Intervals.Min(i => i.Start),
                t.Intervals.Max(i => i.End),
                t.Intervals.Aggregate(TimeSpan.Zero, (sum, i) => sum + i.Duration)))
            .OrderBy(t => t.FirstStart)
            .ToList();

        var intervals = log.Tasks
            .SelectMany(t => t.Intervals.Select(i => new IntervalSummary(t.Task, i.Start, i.End)))
            .OrderBy(i => i.Start)
            .ToList();

        var total = tasks.Aggregate(TimeSpan.Zero, (sum, t) => sum + t.Total);
        return new DaySummary(log.Date, tasks, intervals, total);
    }
}
