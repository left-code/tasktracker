namespace TaskTracker.Models;

/// <summary>All intervals worked on one task during one day.</summary>
public sealed class TaskLog
{
    public string Task { get; set; } = "";

    /// <summary>Redundant with the intervals; stored so the JSON file is readable on its own.</summary>
    public long TotalSeconds { get; set; }

    public List<Interval> Intervals { get; set; } = [];
}
