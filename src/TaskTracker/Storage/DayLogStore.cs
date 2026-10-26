using System.Globalization;
using System.Text.Json;
using TaskTracker.Models;

namespace TaskTracker.Storage;

/// <summary>Reads and writes one JSON file per day: <c>&lt;directory&gt;\yyyy-MM-dd.json</c>.</summary>
public sealed class DayLogStore(string directory)
{
    private readonly Lock _spellingsLock = new();
    private readonly Dictionary<string, (DateOnly Date, string Spelling)> _spellings = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<DateOnly> _unread = [];
    private DateOnly _spellingsBefore = DateOnly.MinValue;

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".TaskTracker", "logs");

    public string Directory => directory;

    public string PathFor(DateOnly date) => Path.Combine(directory, $"{date:yyyy-MM-dd}.json");

    /// <summary>
    /// The day's log as JSON, for <c>TaskTracker report --json</c>. It is loaded and checked like the
    /// report's (so a broken file throws) and its totals are recalculated, so both always agree.
    /// A day without a file gives an empty log.
    /// </summary>
    public string ReadJson(DateOnly date)
    {
        var log = Load(date);
        log.RecalculateTotals();
        return JsonSerializer.Serialize(log, LogJson.Default.DayLog);
    }

    public DayLog Load(DateOnly date)
    {
        var path = PathFor(date);
        if (!File.Exists(path))
            return new DayLog { Date = date };

        DayLog? log;
        try
        {
            log = JsonSerializer.Deserialize(WithRetry(() => File.ReadAllText(path)), LogJson.Default.DayLog);
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
        var json = JsonSerializer.Serialize(log, LogJson.Default.DayLog);
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

    /// <summary>
    /// The name to log a task under: a task already in today's log or an earlier one keeps the
    /// spelling it was logged with, however it is typed now; a new task keeps the typed spelling.
    /// </summary>
    /// <param name="today">Today's log. The app passes its copy in memory, which may be newer than the file.</param>
    public string NameFor(string name, DayLog today) =>
        today.FindTask(name)?.Task ?? KnownSpelling(name, before: today.Date) ?? DayLog.NormalizeName(name);

    /// <inheritdoc cref="NameFor(string, DayLog)"/>
    public string NameFor(string name, DateOnly today) => NameFor(name, TryLoad(today) ?? new DayLog { Date = today });

    /// <summary>
    /// How <paramref name="name"/> (matched case-insensitively) was spelled the last time it was
    /// logged before <paramref name="before"/>, or null if it is a new task.
    /// </summary>
    /// <remarks>
    /// Searches all days, so the first call reads every day file. The names are kept, and later calls
    /// read only days not seen yet: only the running copy writes logs, and it writes today's file, so
    /// days already read don't change. Files that couldn't be read are tried again next time.
    /// </remarks>
    public string? KnownSpelling(string name, DateOnly before)
    {
        lock (_spellingsLock)
        {
            ReadSpellingsBefore(before);
            return _spellings.TryGetValue(DayLog.NormalizeName(name), out var known) ? known.Spelling : null;
        }
    }

    /// <summary>Call under <see cref="_spellingsLock"/>.</summary>
    private void ReadSpellingsBefore(DateOnly before)
    {
        if (before < _spellingsBefore)
        {
            // the clock was set back: start over
            _spellings.Clear();
            _unread.Clear();
            _spellingsBefore = DateOnly.MinValue;
        }

        foreach (var date in LoggedDates().Where(d => d < before && (d >= _spellingsBefore || _unread.Contains(d))))
            AddSpellings(date);
        _spellingsBefore = before;
    }

    private void AddSpellings(DateOnly date)
    {
        var log = TryLoad(date);
        if (log is null)
        {
            _unread.Add(date);
            return;
        }

        _unread.Remove(date);
        foreach (var task in log.Tasks.Where(t => t.Intervals.Count > 0))
        {
            var key = DayLog.NormalizeName(task.Task);
            if (!_spellings.TryGetValue(key, out var known) || known.Date <= date)
                _spellings[key] = (date, task.Task);
        }
    }

    private IEnumerable<DateOnly> LoggedDates() => !System.IO.Directory.Exists(directory) ? [] :
        System.IO.Directory.EnumerateFiles(directory, "????-??-??.json")
            .Select(path => DateOnly.TryParseExact(Path.GetFileNameWithoutExtension(path), "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : (DateOnly?)null)
            .OfType<DateOnly>();

    /// <summary>
    /// <see cref="Load"/>, or null if the file is broken or still locked after the retries: an old
    /// file shouldn't block picking or starting a task.
    /// </summary>
    private DayLog? TryLoad(DateOnly date)
    {
        try
        {
            return Load(date);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Distinct task names from the last <paramref name="days"/> days, most recently worked on first.</summary>
    public IReadOnlyList<string> RecentTaskNames(DateOnly today, int days)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new List<string>();

        for (var i = 0; i < days; i++)
        {
            var byLastWorked = (TryLoad(today.AddDays(-i))?.Tasks ?? [])
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

    /// <summary>
    /// Every task ever logged up to <paramref name="today"/>: first the last <paramref name="recentDays"/> days as
    /// <see cref="RecentTaskNames"/> orders them, then older tasks by the last day they were logged.
    /// Uses the same kept names as <see cref="KnownSpelling"/>, so only the first call reads every file.
    /// </summary>
    public IReadOnlyList<string> AllTaskNames(DateOnly today, int recentDays)
    {
        var names = RecentTaskNames(today, recentDays).ToList();
        var seen = new HashSet<string>(names.Select(DayLog.NormalizeName), StringComparer.OrdinalIgnoreCase);
        lock (_spellingsLock)
        {
            ReadSpellingsBefore(today);
            names.AddRange(_spellings
                .Where(s => !seen.Contains(s.Key))
                .OrderByDescending(s => s.Value.Date)
                .ThenBy(s => s.Value.Spelling, StringComparer.OrdinalIgnoreCase)
                .Select(s => s.Value.Spelling));
        }
        return names;
    }
}
