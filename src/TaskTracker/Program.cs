using System.Runtime.InteropServices;
using System.Text;
using Spectre.Console;
using TaskTracker.Storage;
using TaskTracker.Tracking;
using TaskTracker.UI;

Console.OutputEncoding = Encoding.UTF8;

// Two instances would each rewrite the whole day file every minute and erase each other's work.
using var singleInstance = new Mutex(initiallyOwned: false, $@"Global\TaskTracker-{Environment.UserName}");
try
{
    if (!singleInstance.WaitOne(TimeSpan.Zero))
    {
        AnsiConsole.MarkupLine("[red]TaskTracker is already running.[/] Switch to that window, or quit it first.");
        return 1;
    }
}
catch (AbandonedMutexException)
{
    // The previous instance was killed; we own the mutex now.
}

var time = TimeProvider.System;
var store = new DayLogStore(DayLogStore.DefaultDirectory);

Tracker tracker;
try
{
    tracker = new Tracker(store, time);
}
catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
{
    AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
    AnsiConsole.MarkupLine("[grey]Fix or move the file, then start again.[/]");
    return 1;
}

var heartbeatInterval = TimeSpan.FromMinutes(1);
using var heartbeat = time.CreateTimer(_ =>
{
    try
    {
        tracker.Tick();
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
    {
        // Still locked after Save's own retries, or a broken next-day file at midnight. Unhandled
        // exceptions on a timer thread kill the process; the next beat simply tries again.
    }
}, null, heartbeatInterval, heartbeatInterval);

// Ctrl+C and closing the console window: save the open interval before the process ends.
Console.CancelKeyPress += (_, _) =>
{
    tracker.Stop();
    AnsiConsole.Cursor.Show();
};
using var onWindowClose = PosixSignalRegistration.Create(PosixSignal.SIGHUP, _ => tracker.Stop());

new ConsoleUi(tracker, store).Run();
return 0;
