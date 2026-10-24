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
        [ConsoleKey.P, ConsoleKey.S, ConsoleKey.N, ConsoleKey.T, ConsoleKey.Q];

    public void Run()
    {
        AnsiConsole.Clear();
        var status = tracker.GetStatus();
        if (status.Day.TaskCount > 0)
            WriteSummary(status.Day);

        tracker.Start(PickTask(allowCancel: false)!);

        while (true)
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

                case ConsoleKey.T:
                    ShowTodayScreen();
                    break;

                case ConsoleKey.Q:
                    tracker.Stop();
                    AnsiConsole.Clear();
                    WriteSummary(tracker.GetStatus().Day);
                    AnsiConsole.MarkupLine($"[grey]Log: {Markup.Escape(store.PathFor(tracker.Today))}[/]");
                    return;
            }
        }
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
            $"[grey][[P]] {pauseLabel}   [[S]] switch task   [[N]] new task   [[T]] today   [[Q]] quit[/]";

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

    private void ShowTodayScreen()
    {
        var day = tracker.GetStatus().Day;
        WriteSummary(day);
        AnsiConsole.MarkupLine("[grey][[I]] show all intervals, any other key to return[/]");
        if (Console.ReadKey(intercept: true).Key != ConsoleKey.I)
            return;

        AnsiConsole.Clear();
        WriteIntervals(day);
        AnsiConsole.MarkupLine("[grey]Press any key to return[/]");
        Console.ReadKey(intercept: true);
    }

    private static void WriteSummary(DaySummary day)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title($"Today, {day.Date:ddd dd MMM yyyy}")
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

    private static string FormatDuration(TimeSpan duration) => $"{(int)duration.TotalHours:00}:{duration.Minutes:00}";
}
