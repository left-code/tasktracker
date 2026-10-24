using System.Globalization;
using Spectre.Console;
using TaskTracker.Models;
using TaskTracker.Reporting;
using TaskTracker.Storage;
using TaskTracker.Tracking;

namespace TaskTracker.UI;

/// <summary>The one-shot commands: <c>start</c>, <c>end</c>, <c>report</c> and <c>--help</c>. Each prints and exits.</summary>
public static class CommandLine
{
    private const string Usage =
        "Usage: [bold]TaskTracker[/] [[start <task> | end [[<task>]] | report [[yyyy-MM-dd]]]]   (see [bold]TaskTracker --help[/])";

    private const string AppIsOpen =
        "[red]TaskTracker is open.[/] Switch or stop tasks in that window, or quit it first.";

    public static int Run(string[] args, DayLogStore logs, CommandTracker commands, TimeProvider time)
    {
        // Task names may be typed without quotes: "TaskTracker start Fix login bug".
        var rest = string.Join(' ', args.Skip(1)).Trim();
        var today = LocalClock.DateOf(LocalClock.Now(time));

        switch (args[0].ToLowerInvariant())
        {
            case "--help" or "-h" or "-?" or "/?" or "help" when rest.Length == 0:
                ConsoleUi.WriteHelp(logs.Directory);
                return 0;

            case "start" when rest.Length > 0:
                return Exclusive(AppIsOpen, () => Start(commands, rest, today));

            case "end":
                return Exclusive(AppIsOpen, () => End(commands, rest.Length == 0 ? null : rest, today));

            case "report" when rest.Length == 0:
                // Holding the mutex means the app isn't running, so today's log on disk is final.
                return Exclusive(
                    "[red]TaskTracker is running, so a task may still be ticking.[/] Press [bold]R[/] in that window for the report, or quit it first.",
                    () => Report(logs, commands, today, today));

            case "report" when DateOnly.TryParseExact(rest, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day):
                // Only today can still be ticking in the app, so other days are reported even while it runs.
                return day == today
                    ? Run(["report"], logs, commands, time)
                    : Guarded(() => Report(logs, commands, day, today));

            default:
                AnsiConsole.MarkupLine(Usage);
                return 1;
        }
    }

    private static int Start(CommandTracker commands, string task, DateOnly today)
    {
        var result = commands.Start(task);
        var name = Markup.Escape(result.Open.Task);
        if (result.AlreadyRunning)
        {
            AnsiConsole.MarkupLine($"[yellow]▶ {name} is already running[/] (since {Time(result.Open.Start, today)}).");
            return 0;
        }

        if (result.Ended is not null)
            WriteEnded(result.Ended);
        AnsiConsole.MarkupLine($"[green]▶ Started {name}[/] at {result.Open.Start:HH:mm}. End it with [bold]TaskTracker end[/].");
        return 0;
    }

    private static int End(CommandTracker commands, string? task, DateOnly today)
    {
        var current = commands.Current;
        if (current is null)
        {
            AnsiConsole.MarkupLine("[yellow]No task is running.[/] Start one with [bold]TaskTracker start <task>[/].");
            return 1;
        }
        if (task is not null && !current.Is(task))
        {
            AnsiConsole.MarkupLine(
                $"[red]{Markup.Escape(DayLog.NormalizeName(task))} is not running.[/] " +
                $"{Markup.Escape(current.Task)} is, since {Time(current.Start, today)}.");
            return 1;
        }

        WriteEnded(commands.End()!);
        return 0;
    }

    private static int Report(DayLogStore logs, CommandTracker commands, DateOnly day, DateOnly today)
    {
        ConsoleUi.WriteReport(DaySummary.From(logs.Load(day)));

        // The running task's time is written only when it ends, so it is not in the table yet.
        if (commands.Current is { } open && LocalClock.DateOf(open.Start) <= day && day <= today)
        {
            AnsiConsole.MarkupLine(
                $"[yellow]▶ {Markup.Escape(open.Task)} is running since {Time(open.Start, day)}, not included above.[/] " +
                "[grey]End it with TaskTracker end.[/]");
        }
        if (File.Exists(logs.PathFor(day)))
            AnsiConsole.MarkupLine($"[grey]Log: {Markup.Escape(logs.PathFor(day))}[/]");
        return 0;
    }

    private static void WriteEnded(EndedTask ended)
    {
        AnsiConsole.MarkupLine(
            $"■ Ended [bold]{Markup.Escape(ended.Task)}[/]: [bold]{ConsoleUi.FormatDuration(ended.Worked)}[/] " +
            $"({Time(ended.Start, ended.End)}–{ended.End:HH:mm})");
        if (ended.OffUnknown)
            AnsiConsole.MarkupLine("[yellow]  Could not read the Windows sleep history, so time asleep or switched off may be counted.[/]");
        else if (ended.Off >= TimeSpan.FromMinutes(1))
            AnsiConsole.MarkupLine($"[grey]  {ConsoleUi.FormatDuration(ended.Off)} asleep or switched off, not counted.[/]");
    }

    /// <summary>Runs <paramref name="action"/> only while no other copy is running; the logs are then ours to write.</summary>
    private static int Exclusive(string refusal, Func<int> action)
    {
        using var singleInstance = SingleInstance.TryAcquire();
        if (singleInstance is null)
        {
            AnsiConsole.MarkupLine(refusal);
            return 1;
        }
        return Guarded(action);
    }

    private static int Guarded(Func<int> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
            return 1;
        }
    }

    /// <summary>HH:mm, with the date in front when it isn't the same day as <paramref name="reference"/>.</summary>
    private static string Time(DateTimeOffset time, DateTimeOffset reference) =>
        Time(time, LocalClock.DateOf(reference));

    private static string Time(DateTimeOffset time, DateOnly reference) =>
        LocalClock.DateOf(time) == reference ? $"{time:HH:mm}" : $"{time:dd MMM HH:mm}";
}
