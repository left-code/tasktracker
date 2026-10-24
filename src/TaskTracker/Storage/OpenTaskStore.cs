using System.Text.Json;
using TaskTracker.Models;

namespace TaskTracker.Storage;

/// <summary>
/// Holds the task started with <c>TaskTracker start</c>. Its time is written to the day logs only
/// when it ends, so the log format never has to represent an unfinished interval.
/// </summary>
public sealed class OpenTaskStore(string path)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".TaskTracker", "current.json");

    public string Path => path;

    public OpenTask? Load()
    {
        if (!File.Exists(path))
            return null;

        OpenTask? task;
        try
        {
            task = JsonSerializer.Deserialize<OpenTask>(DayLogStore.WithRetry(() => File.ReadAllText(path)), Options);
        }
        catch (JsonException ex)
        {
            throw Broken($"is not valid JSON: {ex.Message}", ex);
        }
        if (task is null || string.IsNullOrWhiteSpace(task.Task))
            throw Broken("has no task name.");
        if (task.Start == default)
            throw Broken("has no start time.");
        return task;
    }

    /// <summary>An error about current.json that tells the user how to get unstuck.</summary>
    public InvalidDataException Broken(string problem, Exception? inner = null) =>
        new($"'{path}' {problem} Fix it, or delete it to discard the running task.", inner);

    /// <summary>Writes to a temp file first, then swaps it in, like the day logs.</summary>
    public void Save(OpenTask task)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var tempPath = path + ".tmp";
        var json = JsonSerializer.Serialize(task, Options);
        DayLogStore.WithRetry(() =>
        {
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, path, overwrite: true);
            return true;
        });
    }

    public void Delete() => DayLogStore.WithRetry(() =>
    {
        File.Delete(path);
        return true;
    });
}
