namespace TaskTracker.Tracking;

/// <summary>
/// One copy per Windows user may write the logs: two would each rewrite the whole day file and
/// erase each other's work. The commands take it too, so they never write while the app runs.
/// </summary>
public static class SingleInstance
{
    private static string Name => $@"Global\TaskTracker-{Environment.UserName}";

    /// <summary>Returns the owned mutex (dispose it on exit), or null if another copy holds it.</summary>
    public static Mutex? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: false, Name);
        try
        {
            if (mutex.WaitOne(TimeSpan.Zero))
                return mutex;
        }
        catch (AbandonedMutexException)
        {
            return mutex; // the previous copy was killed; we own the mutex now
        }
        mutex.Dispose();
        return null;
    }

    /// <summary>
    /// Whether another copy (the app, or a <c>start</c> or <c>end</c> that is writing) is running. Only
    /// looks, so unlike <see cref="TryAcquire"/> it never makes that copy refuse to start meanwhile.
    /// </summary>
    public static bool IsHeld()
    {
        if (!Mutex.TryOpenExisting(Name, out var mutex))
            return false;
        mutex.Dispose();
        return true;
    }
}
