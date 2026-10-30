using TaskTracker.UI;

namespace TaskTracker.Tests;

public sealed class CommandLineTests
{
    [Fact]
    public void ParseReportArgs_Nothing_IsTodaysPlainReport()
    {
        Assert.Equal(new ReportArgs(null, Json: false, Intervals: false), CommandLine.ParseReportArgs([]));
    }

    [Theory]
    [InlineData("--intervals", "2026-10-23")]
    [InlineData("2026-10-23", "-i")]
    [InlineData("-I", "2026-10-23")]
    public void ParseReportArgs_Intervals_BeforeOrAfterTheDate(string first, string second)
    {
        Assert.Equal(
            new ReportArgs(new DateOnly(2026, 10, 23), Json: false, Intervals: true),
            CommandLine.ParseReportArgs([first, second]));
    }

    [Fact]
    public void ParseReportArgs_Json()
    {
        Assert.Equal(new ReportArgs(null, Json: true, Intervals: false), CommandLine.ParseReportArgs(["--json"]));
    }

    [Theory]
    [InlineData("--json", "--intervals")]
    [InlineData("-i", "--json")]
    public void ParseReportArgs_JsonWithIntervals_IsAccepted(string first, string second)
    {
        // Run writes the JSON and ignores Intervals: the JSON already holds every interval.
        Assert.Equal(new ReportArgs(null, Json: true, Intervals: true), CommandLine.ParseReportArgs([first, second]));
    }

    [Theory]
    [InlineData("-i", "-i")]
    [InlineData("--json", "--json")]
    [InlineData("2026-10-23", "2026-10-24")]
    [InlineData("--verbose")]
    [InlineData("23.10.2026")]
    public void ParseReportArgs_RefusesAnythingElse(params string[] args)
    {
        Assert.Null(CommandLine.ParseReportArgs(args));
    }
}
