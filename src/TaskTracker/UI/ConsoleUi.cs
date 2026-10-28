using System.Globalization;
using System.Reflection;
using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;
using TaskTracker.Reporting;
using TaskTracker.Storage;
using TaskTracker.Tracking;

namespace TaskTracker.UI;

public sealed class ConsoleUi(Tracker tracker, DayLogStore store)
{
    private const int RecentDays = 7;
    private static readonly TimeSpan KeyPollInterval = TimeSpan.FromMilliseconds(200);
    private static readonly ConsoleKey[] ActionKeys =
        [ConsoleKey.P, ConsoleKey.E, ConsoleKey.S, ConsoleKey.N, ConsoleKey.R, ConsoleKey.Q];

    /// <summary>The release number, such as 1.5.0, without the commit hash the SDK appends after a +.</summary>
    private static readonly string Version = typeof(ConsoleUi).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "";

    /// <param name="continueTask">A task started with <c>TaskTracker start</c> to keep timing, instead of asking.</param>
    public void Run(string? continueTask = null)
    {
        AnsiConsole.Clear();
        AnsiConsole.MarkupLine($"[bold]TaskTracker[/] {Markup.Escape(Version)}");
        var status = tracker.GetStatus();
        if (status.Day.TaskCount > 0)
            WriteSummary(status.Day);

        var task = continueTask ?? PickTask(allowCancel: false)!;
        var running = true;
        CarryOnIfSaveFails(() => tracker.Start(task));
        while (running)
            CarryOnIfSaveFails(() => running = HandleNextAction());
    }

    private static void CarryOnIfSaveFails(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // The tracker keeps its state and the heartbeat retries the save, so carry on.
            AnsiConsole.MarkupLine($"[red]Could not save the log:[/] {Markup.Escape(ex.Message)}");
            AnsiConsole.MarkupLine("[grey]Your change is kept and saving is retried every minute. Press any key.[/]");
            Console.ReadKey(intercept: true);
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

            case ConsoleKey.E:
                tracker.Stop();
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
                    if (IsAction(key))
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

    /// <summary>P and E have nothing to act on while idle; ignoring them avoids a redraw that does nothing.</summary>
    private bool IsAction(ConsoleKey key) =>
        ActionKeys.Contains(key) &&
        !(tracker.State == TrackerState.Idle && key is ConsoleKey.P or ConsoleKey.E);

    private static (string Text, IRenderable View) BuildStatusView(TrackerStatus status)
    {
        var task = Markup.Escape(status.CurrentTask ?? "-");
        var duration = FormatDuration(status.CurrentTaskTotal);
        var current = status.State switch
        {
            TrackerState.Running => $"[green]▶ {task}[/]   [bold]{duration}[/]",
            TrackerState.Paused => $"[yellow]⏸ Paused: {task}[/]   [bold]{duration}[/]",
            _ => "[grey]■ No task running[/]",
        };
        var taskKeys = status.State == TrackerState.Idle
            ? "[[S]] start a task"
            : $"[[P]] {(status.State == TrackerState.Running ? "pause" : "resume")}   [[E]] end task   [[S]] switch task";

        var text =
            $"{current}\n" +
            $"Today: [bold]{FormatDuration(status.Day.Total)}[/] across [bold]{status.Day.TaskCount}[/] task(s)\n\n" +
            $"[grey]{taskKeys}   [[N]] new task   [[R]] report   [[Q]] quit[/]";

        var panel = new Panel(new Markup(text))
            .Header($" TaskTracker {Markup.Escape(Version)} — {status.Day.Date:ddd, dd MMM yyyy} ")
            .Border(BoxBorder.Rounded)
            .Padding(2, 1);
        return (text, panel);
    }

    private string? PickTask(bool allowCancel)
    {
        var tasks = store.AllTaskNames(tracker.Today, RecentDays)
            .Where(t => !string.Equals(t, tracker.CurrentTask, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (tasks.Count == 0)
            return AskNewTask(allowCancel);

        var (action, task) = TaskPicker.Pick(tasks, allowCancel);
        return action switch
        {
            PickAction.NewTask => AskNewTask(allowCancel),
            PickAction.Cancel => null,
            _ => task,
        };
    }

    private static string? AskNewTask(bool allowCancel)
    {
        if (!allowCancel)
            return AnsiConsole.Prompt(new TextPrompt<string>("Task name:")).Trim();

        AnsiConsole.Markup("Task name [grey](Esc or empty to cancel)[/]: ");
        var name = ReadLineOrEscape()?.Trim();
        return string.IsNullOrEmpty(name) ? null : name;
    }

    /// <summary>
    /// Reads a line of text, returning null on Esc. Spectre's TextPrompt can't be cancelled with Esc,
    /// so this handles just the keys a task name needs: characters, Backspace, Enter and Esc.
    /// </summary>
    private static string? ReadLineOrEscape()
    {
        var start = Console.GetCursorPosition();
        var text = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            switch (key.Key)
            {
                case ConsoleKey.Escape:
                    Console.WriteLine();
                    return null;

                case ConsoleKey.Enter:
                    Console.WriteLine();
                    return text.ToString();

                case ConsoleKey.Backspace when text.Length > 0:
                    // a whole character, even one made of several chars (emoji, accents)
                    var erased = text.Length - StringInfo.ParseCombiningCharacters(text.ToString())[^1];
                    text.Length -= erased;
                    Redraw(text, start, erased);
                    break;

                default:
                    if (char.IsControl(key.KeyChar))
                        break;
                    text.Append(key.KeyChar);
                    if (!char.IsHighSurrogate(key.KeyChar)) // otherwise wait for the other half
                        Redraw(text, start, erased: 0);
                    break;
            }
        }
    }

    /// <summary>
    /// Writes the text again from where it starts, instead of stepping back one cell per character:
    /// wide characters and the console's delayed wrap at the end of a line make cell counting unreliable.
    /// </summary>
    private static void Redraw(StringBuilder text, (int Left, int Top) start, int erased)
    {
        Console.SetCursorPosition(start.Left, start.Top);
        Console.Write(text.ToString());
        var (left, top) = Console.GetCursorPosition();
        Console.Write(new string(' ', 2 * erased)); // an erased char takes up to two cells
        Console.SetCursorPosition(left, top);
    }

    /// <summary>
    /// Shows today's report while tracking carries on. It is redrawn whenever what it shows changes
    /// (once a minute while a task runs), so the running task stays counted up to now.
    /// Returns false when the user quits from it.
    /// </summary>
    private bool ShowReport()
    {
        while (true)
        {
            var shown = "";
            while (!Console.KeyAvailable)
            {
                var status = tracker.GetStatus();
                var line = ReportStatusLine(status);
                var text = line + string.Concat(status.Day.Tasks.Select(
                    t => $"\n{t.Task}|{t.IntervalCount}|{t.LastEnd:HH:mm}|{FormatDuration(t.Total)}"));
                if (text != shown)
                {
                    shown = text;
                    AnsiConsole.Clear();
                    WriteReport(status.Day);
                    AnsiConsole.MarkupLine(line);
                    AnsiConsole.MarkupLine("[grey][[I]] all intervals   [[Q]] quit   any other key to return[/]");
                }
                Thread.Sleep(KeyPollInterval);
            }

            switch (Console.ReadKey(intercept: true).Key)
            {
                case ConsoleKey.I:
                    AnsiConsole.Clear();
                    WriteIntervals(tracker.GetStatus().Day);
                    AnsiConsole.MarkupLine("[grey]Press any key to return to the report[/]");
                    Console.ReadKey(intercept: true);
                    break;

                case ConsoleKey.Q:
                    Quit();
                    return false;

                default:
                    return true;
            }
        }
    }

    private static string ReportStatusLine(TrackerStatus status)
    {
        var task = Markup.Escape(status.CurrentTask ?? "");
        return status.State switch
        {
            TrackerState.Running => $"[green]▶ {task} is running[/] [grey]({FormatDuration(status.CurrentTaskTotal)} today, counted up to now)[/]",
            TrackerState.Paused => $"[yellow]⏸ {task} is paused[/]",
            _ => "[grey]No task is running.[/]",
        };
    }

    /// <summary>Output of <c>TaskTracker --help</c>.</summary>
    public static void WriteHelp(string logDirectory)
    {
        AnsiConsole.MarkupLine($"[bold]TaskTracker[/] {Markup.Escape(Version)}");
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
        commands.AddRow("TaskTracker report ... --intervals", "Also list every interval (also [bold]-i[/])");
        commands.AddRow("TaskTracker report ... --json", "Print the day's log as JSON, for scripts");
        commands.AddRow("TaskTracker --help", "Show this help (also [bold]-h[/], [bold]-?[/], [bold]/?[/])");
        AnsiConsole.Write(commands);
        AnsiConsole.MarkupLine("[grey]  start on another task switches to it. Quotes around task names are optional.[/]");
        AnsiConsole.MarkupLine("[grey]  Time the PC was asleep or off is read from Windows and not counted.[/]");
        AnsiConsole.MarkupLine("[grey]  While the TaskTracker window is open, use it instead: start and end are[/]");
        AnsiConsole.MarkupLine("[grey]  refused, so that only one copy times tasks. Opening it carries on[/]");
        AnsiConsole.MarkupLine("[grey]  with the task started with start.[/]");
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[bold]Keys while tracking[/]");
        var keys = new Grid().AddColumn(new GridColumn().PadLeft(2).PadRight(4)).AddColumn();
        keys.AddRow("P", "Pause / resume the current task");
        keys.AddRow("E", "End the current task and keep the window open; S or N starts the next one");
        keys.AddRow("S", "Switch task: pick one of the 10 latest (type to search all tasks) or a new one;\nEsc cancels and changes nothing");
        keys.AddRow("N", "Type a new task name (Esc or empty cancels)");
        keys.AddRow("R", "Report: today's summary, the running task counted up to now\n(then I all intervals, Q quit, any other key returns)");
        keys.AddRow("Q", "Stop, save, print today's summary and exit");
        AnsiConsole.Write(keys);
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("Task names are free text, matched ignoring case and surrounding spaces.");
        AnsiConsole.MarkupLine("A task keeps the spelling it was logged with before; a new one keeps yours.");
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

    /// <summary>Every interval of the day in time order. Also used by <c>TaskTracker report --intervals</c>.</summary>
    public static void WriteIntervals(DaySummary day)
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
