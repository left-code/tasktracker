using TaskTracker.UI;

namespace TaskTracker.Tests;

public sealed class TaskPickerTests
{
    private static readonly IReadOnlyList<string> Tasks =
        Enumerable.Range(1, 25).Select(i => $"Task {i}").Append("Review").ToList();

    [Fact]
    public void Filter_EmptyQuery_ShowsTheFirstTenOnly()
    {
        var (shown, more) = TaskPicker.Filter(Tasks, "");

        Assert.Equal(Tasks.Take(10), shown);
        Assert.Equal(16, more);
    }

    [Fact]
    public void Filter_SearchesBeyondTheFirstTen_IgnoringCaseAndSpaces()
    {
        var (shown, more) = TaskPicker.Filter(Tasks, " REVIEW ");

        Assert.Equal(["Review"], shown);
        Assert.Equal(0, more);
    }

    [Fact]
    public void Filter_ManyMatches_ShowsTenInListOrder()
    {
        var (shown, more) = TaskPicker.Filter(Tasks, "task 1");

        // Task 1, Task 10..19 match: 11 in all
        Assert.Equal(["Task 1", .. Enumerable.Range(10, 9).Select(i => $"Task {i}")], shown);
        Assert.Equal(1, more);
    }

    [Fact]
    public void Filter_SmallerMax_ShowsFewerAndCountsTheRest()
    {
        var (shown, more) = TaskPicker.Filter(Tasks, "", max: 3);

        Assert.Equal(["Task 1", "Task 2", "Task 3"], shown);
        Assert.Equal(23, more);
    }

    [Theory]
    [InlineData(30, true, 10)]  // tall window: never more than 10
    [InlineData(16, true, 10)]  // 10 tasks + 6 other lines fit exactly
    [InlineData(15, true, 9)]
    [InlineData(15, false, 10)] // no Cancel line at startup
    [InlineData(8, true, 2)]
    [InlineData(3, true, 1)]    // tiny window: still one task
    public void TasksThatFit_LeavesRoomForTheOtherLines(int windowHeight, bool allowCancel, int expected)
    {
        Assert.Equal(expected, TaskPicker.TasksThatFit(windowHeight, allowCancel));
    }

    [Fact]
    public void Filter_NoMatch_ShowsNothing()
    {
        var (shown, more) = TaskPicker.Filter(Tasks, "deploy");

        Assert.Empty(shown);
        Assert.Equal(0, more);
    }
}
