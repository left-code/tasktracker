using System.Text.Json;
using TaskTracker.Models;

namespace TaskTracker.Storage;

/// <summary>Reads and writes one JSON file per day: <c>&lt;directory&gt;\yyyy-MM-dd.json</c>.</summary>
public sealed class DayLogStore(string directory)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".TaskTracker", "logs");

    public string Directory => directory;

    public string PathFor(DateOnly date) => Path.Combine(directory, $"{date:yyyy-MM-dd}.json");

    public DayLog Load(DateOnly date)
    {
        var path = PathFor(date);
        if (!File.Exists(path))
            return new DayLog { Date = date };

        DayLog? log;
        try
        {
            log = JsonSerializer.Deserialize<DayLog>(WithRetry(() => File.ReadAllText(path)), Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Log file '{path}' is not valid JSON: {ex.Message}", ex);
        }

        // The file name is the source of truth: a missing or wrong "date" would otherwise make the
        // tracker save into another day's file or loop forever looking for the next day.
        log ??= new DayLog();
        log.Date = date;
        log.Tasks ??= [];
        foreach (var task in log.Tasks)
            task.Intervals ??= [];
        return log;
    }

    /// <summary>Writes to a temp file first, then swaps it in, so a crash never leaves a half-written log.</summary>
    public void Save(DayLog log)
    {
        log.RecalculateTotals();
        System.IO.Directory.CreateDirectory(directory);

        var path = PathFor(log.Date);
        var tempPath = path + ".tmp";
        var json = JsonSerializer.Serialize(log, Options);
        WithRetry(() =>
        {
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, path, overwrite: true);
            return true;
        });
    }

    /// <summary>
    /// Antivirus, sync tools and the search indexer briefly lock files. On Windows that surfaces as
    /// IOException or UnauthorizedAccessException; a short retry rides it out.
    /// </summary>
    internal static T WithRetry<T>(Func<T> action)
    {
        const int attempts = 4;
        for (var i = 1; ; i++)
        {
            try
            {
                return action();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && i < attempts)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(75));
            }
        }
    }

    /// <summary>Distinct task names from the last <paramref name="days"/> days, most recently worked on first.</summary>
    public IReadOnlyList<string> RecentTaskNames(DateOnly today, int days)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new List<string>();

        for (var i = 0; i < days; i++)
        {
            DayLog log;
            try
            {
                log = Load(today.AddDays(-i));
            }
            catch (InvalidDataException)
            {
                continue; // a broken old file shouldn't block picking a task
            }

            var byLastWorked = log.Tasks
                .Where(t => t.Intervals.Count > 0)
                .OrderByDescending(t => t.Intervals.Max(iv => iv.End));
            foreach (var task in byLastWorked)
            {
                if (seen.Add(task.Task))
                    names.Add(task.Task);
            }
        }
        return names;
    }
}
