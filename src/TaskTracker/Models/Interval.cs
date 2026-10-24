using System.Text.Json.Serialization;

namespace TaskTracker.Models;

/// <summary>One continuous stretch of work on a task.</summary>
public sealed class Interval
{
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset End { get; set; }

    /// <summary>Redundant with Start/End; stored so the JSON file is readable on its own.</summary>
    public long DurationSeconds { get; set; }

    [JsonIgnore]
    public TimeSpan Duration => End - Start;
}
