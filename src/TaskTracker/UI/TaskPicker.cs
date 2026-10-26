using System.Globalization;
using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace TaskTracker.UI;

public enum PickAction { Task, NewTask, Cancel }

/// <summary>
/// Picks a task from a list that shows at most <see cref="MaxShown"/> tasks but searches all of them.
/// Spectre's SelectionPrompt can't do this: it scrolls to every choice it is given.
/// </summary>
public static class TaskPicker
{
    public const int MaxShown = 10;
    private const string NewTaskLabel = "+ New task...";
    private const string CancelLabel = "<- Cancel";

    /// <summary>
    /// The first <paramref name="max"/> tasks that contain <paramref name="query"/> (ignoring case and
    /// surrounding spaces), in list order, and how many more match.
    /// </summary>
    public static (IReadOnlyList<string> Shown, int More) Filter(IReadOnlyList<string> tasks, string query, int max = MaxShown)
    {
        var text = query.Trim();
        var matches = tasks.Where(t => t.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
        return (matches.Take(max).ToList(), Math.Max(0, matches.Count - max));
    }

    /// <summary>
    /// How many tasks fit: Live crops what is taller than the window, and the selection could then move
    /// onto a row that isn't shown. Besides the tasks there are the title, the search, New task, the note
    /// below the list, Cancel if offered, and the cursor's line.
    /// </summary>
    public static int TasksThatFit(int windowHeight, bool allowCancel) =>
        Math.Clamp(windowHeight - (allowCancel ? 6 : 5), 1, MaxShown);

    /// <param name="tasks">All tasks, most recently used first.</param>
    /// <param name="allowCancel">Adds a Cancel choice, and Esc cancels; otherwise Esc clears the search.</param>
    public static (PickAction Action, string? Task) Pick(IReadOnlyList<string> tasks, bool allowCancel)
    {
        var query = new StringBuilder();
        var selected = 0;
        var queryChanged = false;
        (PickAction, string?) result = (PickAction.Cancel, null);

        AnsiConsole.Live(new Markup("")).AutoClear(true).Start(ctx =>
        {
            while (true)
            {
                var (shown, more) = Filter(tasks, query.ToString(), TasksThatFit(Console.WindowHeight, allowCancel));
                var items = new List<(PickAction Action, string? Task)> { (PickAction.NewTask, null) };
                items.AddRange(shown.Select(t => (PickAction.Task, (string?)t)));
                if (allowCancel)
                    items.Add((PickAction.Cancel, null));

                if (queryChanged)
                {
                    // like Spectre's search: jump to the first match, or to New task if nothing matches
                    selected = query.Length > 0 && shown.Count > 0 ? 1 : 0;
                    queryChanged = false;
                }
                selected = Math.Clamp(selected, 0, items.Count - 1);

                ctx.UpdateTarget(Render(query.ToString(), items, selected, more, allowCancel));
                ctx.Refresh();

                var key = Console.ReadKey(intercept: true);
                switch (key.Key)
                {
                    case ConsoleKey.UpArrow:
                        selected--;
                        break;

                    case ConsoleKey.DownArrow:
                        selected++;
                        break;

                    case ConsoleKey.Enter:
                        result = items[selected];
                        return;

                    case ConsoleKey.Escape when allowCancel:
                        result = (PickAction.Cancel, null);
                        return;

                    case ConsoleKey.Escape:
                        query.Clear();
                        queryChanged = true;
                        break;

                    case ConsoleKey.Backspace when query.Length > 0:
                        // a whole character, even one made of several chars (emoji, accents)
                        query.Length = StringInfo.ParseCombiningCharacters(query.ToString())[^1];
                        queryChanged = true;
                        break;

                    default:
                        if (char.IsControl(key.KeyChar))
                            break;
                        query.Append(key.KeyChar);
                        if (char.IsHighSurrogate(key.KeyChar)) // the other half follows straight away
                            query.Append(Console.ReadKey(intercept: true).KeyChar);
                        queryChanged = true;
                        break;
                }
            }
        });
        return result;
    }

    private static IRenderable Render(
        string query, List<(PickAction Action, string? Task)> items, int selected, int more, bool allowCancel)
    {
        var lines = new List<string>
        {
            allowCancel ? "Select a task [grey](type to search all tasks, Esc to cancel)[/]:" : "Select a task [grey](type to search all tasks)[/]:",
            query.Length == 0 ? "[grey]Search:[/]" : $"[grey]Search:[/] [blue]{Markup.Escape(query)}[/]",
        };
        for (var i = 0; i < items.Count; i++)
        {
            var label = Markup.Escape(items[i].Action switch
            {
                PickAction.NewTask => NewTaskLabel,
                PickAction.Cancel => CancelLabel,
                _ => items[i].Task!,
            });
            lines.Add(i == selected ? $"[blue]> {label}[/]" : $"  {label}");
        }
        if (query.Length > 0 && !items.Any(i => i.Action == PickAction.Task))
            lines.Add("[grey]  No task matches.[/]");
        if (more > 0)
            lines.Add(query.Length == 0
                ? $"[grey]  {more} more task(s) not shown, type to search[/]"
                : $"[grey]  {more} more match(es), type more to narrow down[/]");
        return new Markup(string.Join('\n', lines));
    }
}
