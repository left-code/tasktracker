using System.Runtime.InteropServices;
using System.Text;
using Spectre.Console;
using TaskTracker.Storage;
using TaskTracker.Tracking;
using TaskTracker.UI;

Console.OutputEncoding = Encoding.UTF8;

var time = TimeProvider.System;
var store = new DayLogStore(DayLogStore.DefaultDirectory);
IPowerHistory power = OperatingSystem.IsWindows() ? new EventLogPowerHistory() : new NoPowerHistory();
var commands = new CommandTracker(store, new OpenTaskStore(OpenTaskStore.DefaultPath), power, time);

if (args.Length > 0)
    return CommandLine.Run(args, store, commands, time);

using var singleInstance = SingleInstance.TryAcquire();
if (singleInstance is null)
{
    AnsiConsole.MarkupLine("[red]TaskTracker is already running.[/] Switch to that window, or quit it first.");
    return 1;
}

string? continueTask;
Tracker tracker;
try
{
    // A task started with "TaskTracker start" carries on here: record it up to now, then keep timing it.
    var pickedUp = commands.End();
    if (pickedUp is { OffUnknown: true })
    {
        AnsiConsole.MarkupLine($"[yellow]Recorded {Markup.Escape(pickedUp.Task)} so far, but could not read the Windows sleep history,[/]");
        AnsiConsole.MarkupLine("[yellow]so time asleep or switched off since it started may be counted.[/] [grey]Press any key.[/]");
        Console.ReadKey(intercept: true);
    }
    continueTask = pickedUp?.Task;
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

new ConsoleUi(tracker, store).Run(continueTask);
return 0;
