using System.Runtime.InteropServices;
using System.Text;
using Spectre.Console;
using TaskTracker.Storage;
using TaskTracker.Tracking;
using TaskTracker.UI;

Console.OutputEncoding = Encoding.UTF8;

var time = TimeProvider.System;
var store = new DayLogStore(DayLogStore.DefaultDirectory);

Tracker tracker;
try
{
    tracker = new Tracker(store, time);
}
catch (InvalidDataException ex)
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
    catch (IOException)
    {
        // File briefly locked (antivirus, sync tool); the next beat retries.
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
