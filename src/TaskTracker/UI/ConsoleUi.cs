using System.Reflection;
using Spectre.Console;
using Spectre.Console.Rendering;
using TaskTracker.Reporting;
using TaskTracker.Storage;
using TaskTracker.Tracking;

namespace TaskTracker.UI;

public sealed class ConsoleUi(Tracker tracker, DayLogStore store)
{
    private const int RecentDays = 7;
    private const string NewTaskChoice = "+ New task...";
    private const string CancelChoice = "<- Cancel";
    private static readonly TimeSpan KeyPollInterval = TimeSpan.FromMilliseconds(200);
    private static readonly ConsoleKey[] ActionKeys =
        [ConsoleKey.P, ConsoleKey.S, ConsoleKey.N, ConsoleKey.R, ConsoleKey.Q];

    /// <param name="continueTask">A task started with <c>TaskTracker start</c> to keep timing, instead of asking.</param>
    public void Run(string? continueTask = null)
    {
        AnsiConsole.Clear();
        var status = tracker.GetStatus();
        if (status.Day.TaskCount > 0)
            WriteSummary(status.Day);

        tracker.Start(continueTask ?? PickTask(allowCancel: false)!);

        while (true)
        {
            try
            {
                if (!HandleNextAction())
                    return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                // The tracker keeps its state and the heartbeat retries the save, so carry on.
                AnsiConsole.MarkupLine($"[red]Could not save the log:[/] {Markup.Escape(ex.Message)}");
                AnsiConsole.MarkupLine("[grey]Tracking continues and saving is retried every minute. Press any key.[/]");
                Console.ReadKey(intercept: true);
            }
        }
    }

    /// <summary>Waits for one key action and performs it. Returns false when the user quits.</summary>
    private bool HandleNextAction()
    {
        AnsiConsole.Clear();
        switch (WaitForAction())
        {
            case ConsoleKey.P:
                if (tracker.State == TrackerState.Running)
                    tracker.Pause();
                else
                    tracker.Resume();
                break;

            case ConsoleKey.S:
                StartIfChosen(PickTask(allowCancel: true));
                break;

            case ConsoleKey.N:
                StartIfChosen(AskNewTask(allowCancel: true));
                break;

            case ConsoleKey.R:
                return ShowReport();

            case ConsoleKey.Q:
                Quit();
                return false;
        }
        return true;
    }

    /// <summary>Stops and saves, retrying once on failure; exits either way, since the heartbeat stops with the app.</summary>
    private void Quit()
    {
        AnsiConsole.Clear();
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                tracker.Stop();
                break;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 2)
            {
                AnsiConsole.MarkupLine($"[yellow]Saving failed ({Markup.Escape(ex.Message)}), retrying...[/]");
                Thread.Sleep(TimeSpan.FromSeconds(1));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AnsiConsole.MarkupLine($"[red]Could not save the last interval:[/] {Markup.Escape(ex.Message)}");
                AnsiConsole.MarkupLine("[grey]At most the last minute is missing from the log.[/]");
                break;
            }
        }

        WriteReport(tracker.GetStatus().Day);
        AnsiConsole.MarkupLine($"[grey]Log: {Markup.Escape(store.PathFor(tracker.Today))}[/]");
    }

    private void StartIfChosen(string? task)
    {
        if (task is not null)
            tracker.Start(task);
    }

    /// <summary>Shows the live status until an action key is pressed. Redraws only when the text changes (once a minute).</summary>
    private ConsoleKey WaitForAction()
    {
        var pressed = ConsoleKey.Q;
        var (text, view) = BuildStatusView(tracker.GetStatus());

        AnsiConsole.Live(view).Start(ctx =>
        {
            // Live draws nothing until the first refresh; without this the screen stays blank
            // until the status text first changes (up to a minute).
            ctx.Refresh();
            while (true)
            {
                while (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(intercept: true).Key;
                    if (ActionKeys.Contains(key))
                    {
                        pressed = key;
                        return;
                    }
                }

                var (newText, newView) = BuildStatusView(tracker.GetStatus());
                if (newText != text)
                {
                    text = newText;
                    ctx.UpdateTarget(newView);
                    ctx.Refresh();
                }
                Thread.Sleep(KeyPollInterval);
            }
        });
        return pressed;
    }

    private static (string Text, IRenderable View) BuildStatusView(TrackerStatus status)
    {
        var task = Markup.Escape(status.CurrentTask ?? "-");
        var current = status.State == TrackerState.Running
            ? $"[green]▶ {task}[/]   [bold]{FormatDuration(status.CurrentTaskTotal)}[/]"
            : $"[yellow]⏸ Paused: {task}[/]   [bold]{FormatDuration(status.CurrentTaskTotal)}[/]";
        var pauseLabel = status.State == TrackerState.Running ? "pause" : "resume";

        var text =
            $"{current}\n" +
            $"Today: [bold]{FormatDuration(status.Day.Total)}[/] across [bold]{status.Day.TaskCount}[/] task(s)\n\n" +
            $"[grey][[P]] {pauseLabel}   [[S]] switch task   [[N]] new task   [[R]] report   [[Q]] quit[/]";

        var panel = new Panel(new Markup(text))
            .Header($" TaskTracker — {status.Day.Date:ddd, dd MMM yyyy} ")
            .Border(BoxBorder.Rounded)
            .Padding(2, 1);
        return (text, panel);
    }

    private string? PickTask(bool allowCancel)
    {
        var recent = store.RecentTaskNames(tracker.Today, RecentDays)
            .Where(t => !string.Equals(t, tracker.CurrentTask, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (recent.Count == 0)
            return AskNewTask(allowCancel);

        var prompt = new SelectionPrompt<string>()
            .Title("Select a task [grey](type to search)[/]:")
            .PageSize(15)
            .EnableSearch()
            .UseConverter(Markup.Escape)
            .AddChoices(NewTaskChoice)
            .AddChoices(recent);
        if (allowCancel)
            prompt.AddChoice(CancelChoice);

        var choice = AnsiConsole.Prompt(prompt);
        return choice switch
        {
            NewTaskChoice => AskNewTask(allowCancel),
            CancelChoice => null,
            _ => choice,
        };
    }

    private static string? AskNewTask(bool allowCancel)
    {
        var prompt = new TextPrompt<string>(allowCancel ? "Task name [grey](empty to cancel)[/]:" : "Task name:");
        if (allowCancel)
            prompt.AllowEmpty();

        var name = AnsiConsole.Prompt(prompt).Trim();
        return name.Length == 0 ? null : name;
    }

    /// <summary>
    /// Stops tracking and shows today's report until the user starts a task (returns true) or quits (false).
    /// </summary>
    private bool ShowReport()
    {
        DaySummary day;
        string? warning = null;
        try
        {
            day = tracker.StopForReport();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // A failed midnight save aborts Stop before it stops: the task is still ticking, so no report.
            if (tracker.State != TrackerState.Idle)
                throw;

            // Stopped in memory, but the log could not be saved or read back. Show the in-memory
            // times; the heartbeat retries the save.
            day = tracker.GetStatus().Day;
            warning = $"[yellow]Could not save or read back the log:[/] {Markup.Escape(ex.Message)}\n" +
                      "[grey]Showing times from memory. Saving is retried every minute.[/]";
        }

        while (true)
        {
            AnsiConsole.Clear();
            WriteReport(day);
            if (warning is not null)
                AnsiConsole.MarkupLine(warning);
            AnsiConsole.MarkupLine("[grey]Tracking is stopped.   [[I]] all intervals   [[Q]] quit   any other key to start a task[/]");

            switch (Console.ReadKey(intercept: true).Key)
            {
                case ConsoleKey.I:
                    AnsiConsole.Clear();
                    WriteIntervals(day);
                    AnsiConsole.MarkupLine("[grey]Press any key to return[/]");
                    Console.ReadKey(intercept: true);
                    break;

                case ConsoleKey.Q:
                    Quit();
                    return false;

                default:
                    AnsiConsole.Clear();
                    string? task;
                    try
                    {
                        task = PickTask(allowCancel: true);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // Reading recent tasks failed; stay on the report rather than show a stopped
                        // tracker as if it were paused.
                        warning = $"[yellow]Could not read recent tasks:[/] {Markup.Escape(ex.Message)}";
                        break;
                    }
                    if (task is null)
                        break; // cancelled: back to the report
                    tracker.Start(task);
                    return true;
            }
        }
    }

    /// <summary>Output of <c>TaskTracker --help</c>.</summary>
    public static void WriteHelp(string logDirectory)
    {
        var version = typeof(ConsoleUi).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0];

        AnsiConsole.MarkupLine($"[bold]TaskTracker[/] {Markup.Escape(version ?? "")}");
        AnsiConsole.MarkupLine("Tracks how much time you spend on tasks. Pick or type a task and a timer starts;");
        AnsiConsole.MarkupLine("switch, pause and report while you work. Each day is saved as one JSON log.");
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[bold]Usage[/]");
        var commands = new Grid().AddColumn(new GridColumn().PadLeft(2).PadRight(4)).AddColumn();
        commands.AddRow("TaskTracker", "Start tracking (asks for a task first)");
        commands.AddRow("TaskTracker start <task>", "Start a task without keeping a window open");
        commands.AddRow("TaskTracker end [[<task>]]", "End the task started with [bold]start[/]");
        commands.AddRow("TaskTracker report", "Print today's summary and exit");
        commands.AddRow("TaskTracker report [[yyyy-MM-dd]]", "Print another day's summary");
        commands.AddRow("TaskTracker --help", "Show this help (also [bold]-h[/], [bold]-?[/], [bold]/?[/])");
        AnsiConsole.Write(commands);
        AnsiConsole.MarkupLine("[grey]  start on another task switches to it. Quotes around task names are optional.[/]");
        AnsiConsole.MarkupLine("[grey]  Time the PC was asleep or off is read from Windows and not counted.[/]");
        AnsiConsole.MarkupLine("[grey]  While the TaskTracker window is open, use it instead: start, end and today's[/]");
        AnsiConsole.MarkupLine("[grey]  report are refused, so that only one copy times tasks. Opening it carries on[/]");
        AnsiConsole.MarkupLine("[grey]  with the task started with start.[/]");
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[bold]Keys while tracking[/]");
        var keys = new Grid().AddColumn(new GridColumn().PadLeft(2).PadRight(4)).AddColumn();
        keys.AddRow("P", "Pause / resume the current task");
        keys.AddRow("S", "Switch task: pick a recent one (type to search) or a new one");
        keys.AddRow("N", "Type a new task name");
        keys.AddRow("R", "Report: stop tracking, show today's summary\n(then I all intervals, Q quit, any other key picks a task)");
        keys.AddRow("Q", "Stop, save, print today's summary and exit");
        AnsiConsole.Write(keys);
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("Task names are free text, matched ignoring case and surrounding spaces.");
        AnsiConsole.MarkupLine("Sleep over 5 minutes is not counted. Only one copy runs at a time.");
        AnsiConsole.MarkupLine($"Logs: [grey]{Markup.Escape(Path.Combine(logDirectory, "yyyy-MM-dd.json"))}[/]");
    }

    /// <summary>The day's summary, or a short note for an empty day. Also used by <c>TaskTracker report</c>.</summary>
    public static void WriteReport(DaySummary day)
    {
        if (day.TaskCount == 0)
            AnsiConsole.MarkupLine($"[grey]Nothing tracked on {day.Date:ddd dd MMM yyyy}.[/]");
        else
            WriteSummary(day);
    }

    private static void WriteSummary(DaySummary day)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title($"{day.Date:dddd, dd MMM yyyy}")
            .AddColumn(new TableColumn("Task").Footer($"[bold]{day.TaskCount} task(s)[/]"))
            .AddColumn(new TableColumn("Intervals").RightAligned())
            .AddColumn(new TableColumn("First start").Centered())
            .AddColumn(new TableColumn("Last end").Centered())
            .AddColumn(new TableColumn("Total").RightAligned().Footer($"[bold]{FormatDuration(day.Total)}[/]"));

        foreach (var task in day.Tasks)
        {
            table.AddRow(
                Markup.Escape(task.Task),
                task.IntervalCount.ToString(),
                $"{task.FirstStart:HH:mm}",
                $"{task.LastEnd:HH:mm}",
                FormatDuration(task.Total));
        }
        AnsiConsole.Write(table);
    }

    private static void WriteIntervals(DaySummary day)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title($"Intervals, {day.Date:ddd dd MMM yyyy}")
            .AddColumn("Start")
            .AddColumn("End")
            .AddColumn(new TableColumn("Duration").RightAligned())
            .AddColumn("Task");

        foreach (var interval in day.Intervals)
        {
            table.AddRow(
                $"{interval.Start:HH:mm}",
                $"{interval.End:HH:mm}",
                FormatDuration(interval.Duration),
                Markup.Escape(interval.Task));
        }
        AnsiConsole.Write(table);
    }

    internal static string FormatDuration(TimeSpan duration) => $"{(int)duration.TotalHours:00}:{duration.Minutes:00}";
}
