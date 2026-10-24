using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Runtime.Versioning;
using System.Xml.Linq;

namespace TaskTracker.Tracking;

public sealed record TimeRange(DateTimeOffset Start, DateTimeOffset End);

/// <summary>When the PC was asleep, hibernating or switched off.</summary>
public interface IPowerHistory
{
    /// <summary>Periods overlapping <paramref name="from"/>..<paramref name="to"/>, or null if the history can't be read.</summary>
    IReadOnlyList<TimeRange>? OffPeriods(DateTimeOffset from, DateTimeOffset to);
}

/// <summary>
/// Reads sleep, shutdown and Modern Standby times from the Windows System event log. A task started
/// with <c>TaskTracker start</c> has no process watching the clock, so this is how time the PC
/// spent off is kept out of the log.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class EventLogPowerHistory : IPowerHistory
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/win/2004/08/events/event";

    public IReadOnlyList<TimeRange>? OffPeriods(DateTimeOffset from, DateTimeOffset to)
    {
        try
        {
            var periods = new List<TimeRange>();

            // Sleep and hibernate: one event on wake carries both times.
            foreach (var e in Query("Microsoft-Windows-Power-Troubleshooter", "EventID=1", from, to))
            {
                if (Data(e, "SleepTime") is { } sleep && Data(e, "WakeTime") is { } wake)
                    periods.Add(new TimeRange(sleep, wake));
            }

            // Shutdown or restart: the boot event carries the start time. The shutdown time is taken
            // from the last event logged before it, which also covers a crash or power loss.
            foreach (var e in Query("Microsoft-Windows-Kernel-General", "EventID=12", from, to))
            {
                if (Data(e, "StartTime") is { } boot)
                    periods.Add(new TimeRange(LastEventBefore(boot) ?? from, boot));
            }

            // Modern Standby (S0 low power idle) logs entering and leaving as separate events.
            DateTimeOffset? entered = null;
            foreach (var e in Query("Microsoft-Windows-Kernel-Power", "(EventID=506 or EventID=507)", from, to))
            {
                if (e.Id == 506)
                    entered = e.Created;
                else if (entered is not null && e.Created is { } left)
                {
                    periods.Add(new TimeRange(entered.Value, left));
                    entered = null;
                }
            }
            return periods;
        }
        catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>What is needed from one event record, copied out so the record can be disposed right away.</summary>
    private sealed record Event(int Id, DateTimeOffset? Created, Dictionary<string, string> Data);

    private static List<Event> Query(string provider, string ids, DateTimeOffset from, DateTimeOffset to)
    {
        var xpath = $"*[System[Provider[@Name='{provider}'] and {ids} and " +
                    $"TimeCreated[@SystemTime>='{Utc(from)}' and @SystemTime<='{Utc(to)}']]]";
        return Read(new EventLogQuery("System", PathType.LogName, xpath));
    }

    private static DateTimeOffset? LastEventBefore(DateTimeOffset time)
    {
        var xpath = $"*[System[TimeCreated[@SystemTime<'{Utc(time)}']]]";
        using var reader = new EventLogReader(new EventLogQuery("System", PathType.LogName, xpath) { ReverseDirection = true });
        using var last = reader.ReadEvent();
        return last?.TimeCreated;
    }

    private static List<Event> Read(EventLogQuery query)
    {
        var events = new List<Event>();
        using var reader = new EventLogReader(query);
        while (reader.ReadEvent() is { } record)
        {
            using (record)
            {
                var data = new Dictionary<string, string>();
                foreach (var d in XDocument.Parse(record.ToXml()).Descendants(Ns + "Data"))
                {
                    if ((string?)d.Attribute("Name") is { } name)
                        data.TryAdd(name, d.Value);
                }
                events.Add(new Event(record.Id, record.TimeCreated, data));
            }
        }
        return events;
    }

    private static DateTimeOffset? Data(Event e, string name) =>
        e.Data.TryGetValue(name, out var value) &&
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;

    private static string Utc(DateTimeOffset time) => time.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
}

/// <summary>Used where the event log isn't available: nothing is known, so nothing is left out.</summary>
public sealed class NoPowerHistory : IPowerHistory
{
    public IReadOnlyList<TimeRange>? OffPeriods(DateTimeOffset from, DateTimeOffset to) => null;
}
