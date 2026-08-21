using PinballScores.Core.Api;
using PinballScores.Core.Insertion;
using Xunit;

namespace PinballScores.Tests;

public class SlotPlannerTests
{
    private static RemoteScore Score(string? category, string initials, long value) =>
        new() { Table = "smanve_101", Category = category, Initials = initials, Value = value.ToString() };

    [Fact]
    public void HighestScoreGoesToTheFirstPhysicalSlot()
    {
        var map = TestData.Catalog.Find("smanve_101")!;
        var board = new[]
        {
            Score(null, "AAA", 10_000),
            Score(null, "BBB", 90_000),
            Score(null, "CCC", 50_000),
        };

        var plan = SlotPlanner.Plan(map, board).Where(a => a.Category is null).ToList();

        // Rank and slot are the same axis, so this is an index-for-index zip.
        Assert.Equal("Grand Champion", plan[0].SlotLabel);
        Assert.Equal("BBB", plan[0].Initials);
        Assert.Equal("First Place", plan[1].SlotLabel);
        Assert.Equal("CCC", plan[1].Initials);
        Assert.Equal("AAA", plan[2].Initials);
    }

    [Fact]
    public void NeverPlansMoreScoresThanTheMachineHasSlots()
    {
        var map = TestData.Catalog.Find("smanve_101")!;
        var board = Enumerable.Range(1, 40).Select(i => Score(null, $"P{i:00}", i * 1000)).ToArray();

        var plan = SlotPlanner.Plan(map, board).Where(a => a.Category is null).ToList();

        Assert.Equal(map.HighScores.Count, plan.Count);
    }

    [Fact]
    public void NamedCategoriesAreAssignedToTheirOwnSlots()
    {
        var map = TestData.Catalog.Find("smanve_101")!;
        var board = new[] { Score("spider_champion", "ZZZ", 42) };

        var plan = SlotPlanner.Plan(map, board).ToList();

        var spider = Assert.Single(plan, a => a.Category == "spider_champion");
        Assert.Equal("ZZZ", spider.Initials);
        Assert.Equal(42, spider.Value);
    }

    [Fact]
    public void ShortBoardBlanksTheRemainingSlots()
    {
        var map = TestData.Catalog.Find("smanve_101")!;

        var plan = SlotPlanner.Plan(map, [Score(null, "AAA", 1)]).Where(a => a.Category is null).ToList();

        // Every slot is planned: the machine must end up showing exactly what the
        // API holds, so a score it doesn't know about cannot be left in place.
        Assert.Equal(map.HighScores.Count, plan.Count);
        Assert.Equal("Grand Champion", plan[0].SlotLabel);
        Assert.Equal("AAA", plan[0].Initials);
        Assert.False(plan[0].IsPlaceholder);
        Assert.All(plan.Skip(1), a => Assert.True(a.IsPlaceholder));
    }
}
