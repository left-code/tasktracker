namespace TaskTracker.Tracking;

/// <summary>
/// One copy per Windows user may write the logs: two would each rewrite the whole day file and
/// erase each other's work. The commands take it too, so they never write while the app runs.
/// </summary>
public static class SingleInstance
{
    /// <summary>Returns the owned mutex (dispose it on exit), or null if another copy holds it.</summary>
    public static Mutex? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: false, $@"Global\TaskTracker-{Environment.UserName}");
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
}
