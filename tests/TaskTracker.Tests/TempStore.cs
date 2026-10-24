using TaskTracker.Storage;

namespace TaskTracker.Tests;

/// <summary>A <see cref="DayLogStore"/> in a throwaway directory.</summary>
public sealed class TempStore : IDisposable
{
    public TempStore()
    {
        Directory = Path.Combine(Path.GetTempPath(), "TaskTracker.Tests", Guid.NewGuid().ToString("N"));
        Store = new DayLogStore(Directory);
    }

    public string Directory { get; }
    public DayLogStore Store { get; }

    public void Dispose()
    {
        if (System.IO.Directory.Exists(Directory))
            System.IO.Directory.Delete(Directory, recursive: true);
    }
}
