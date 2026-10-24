namespace TaskTracker.Models;

/// <summary>A task started with <c>TaskTracker start</c> that has not ended yet; persisted as <c>current.json</c>.</summary>
public sealed class OpenTask
{
    public string Task { get; set; } = "";
    public DateTimeOffset Start { get; set; }

    /// <summary>Same matching as task names in the log: case-insensitive, ignoring surrounding spaces.</summary>
    public bool Is(string name) =>
        string.Equals(Task, DayLog.NormalizeName(name), StringComparison.OrdinalIgnoreCase);
}
