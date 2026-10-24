namespace TaskTracker.Models;

/// <summary>Everything tracked on one calendar day; persisted as one JSON file.</summary>
public sealed class DayLog
{
    public DateOnly Date { get; set; }
    public List<TaskLog> Tasks { get; set; } = [];

    public static string NormalizeName(string name) => name.Trim();

    public TaskLog? FindTask(string name)
    {
        var normalized = NormalizeName(name);
        return Tasks.Find(t => string.Equals(t.Task, normalized, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Finds a task case-insensitively; a new task keeps the spelling it was first entered with.</summary>
    public TaskLog GetOrAddTask(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var task = FindTask(name);
        if (task is null)
        {
            task = new TaskLog { Task = NormalizeName(name) };
            Tasks.Add(task);
        }
        return task;
    }

    public void RecalculateTotals()
    {
        foreach (var task in Tasks)
        {
            foreach (var interval in task.Intervals)
                interval.DurationSeconds = (long)interval.Duration.TotalSeconds;
            task.TotalSeconds = task.Intervals.Sum(i => i.DurationSeconds);
        }
    }
}
