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

        try
        {
            return JsonSerializer.Deserialize<DayLog>(File.ReadAllText(path), Options) ?? new DayLog { Date = date };
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Log file '{path}' is not valid JSON: {ex.Message}", ex);
        }
    }

    /// <summary>Writes to a temp file first, then swaps it in, so a crash never leaves a half-written log.</summary>
    public void Save(DayLog log)
    {
        log.RecalculateTotals();
        System.IO.Directory.CreateDirectory(directory);

        var path = PathFor(log.Date);
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(log, Options));
        File.Move(tempPath, path, overwrite: true);
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
