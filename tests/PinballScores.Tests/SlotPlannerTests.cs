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
    public void CategoriesAreMatchedInTheApisOwnSpelling()
    {
        // What the live API returns for a category is the label it was first stored
        // under — "SPIDER CHAMPION", not the map's "spider_champion". Compared
        // literally, no row matched and every category slot on the machine was
        // planned as a blank.
        var map = TestData.Catalog.Find("smanve_101")!;
        var board = new[] { Score("SPIDER CHAMPION", "ZZZ", 42) };

        var plan = SlotPlanner.Plan(map, board).ToList();

        var spider = Assert.Single(plan, a => a.Category == "spider_champion");
        Assert.Equal("ZZZ", spider.Initials);
        Assert.False(spider.IsPlaceholder);
    }

    [Fact]
    public void MedievalMadnessFillsEveryChampionSlotFromTheApiBoard()
    {
        // Its eleven champion slots are the largest set of category slots on the
        // cabinet, and all eleven were being blanked.
        var map = TestData.Catalog.Find("mm_109c")!;
        var board = new[]
        {
            Score(null, "OLE", 102_283_300),
            Score("CASTLE CHAMPION", "IDA", 29),
            Score("JOUST CHAMPION", "JKB", 32),
            Score("CATAPULT CHAMPION", "AND", 19),
            Score("PEASANT CHAMPION", "RUN", 8),
            Score("DAMSEL CHAMPION", "TBO", 30),
            Score("TROLL CHAMPION", "JKB", 32),
            Score("MADNESS CHAMPION", "LSB", 14_614_120),
            Score("KING OF THE REALM", "KAS", 16),
            Score("KING OF THE REALM", "VIB", 14),
            Score("KING OF THE REALM", "AND", 11),
            Score("KING OF THE REALM", "PTR", 8),
        };

        var plan = SlotPlanner.Plan(map, board).Where(a => a.Category is not null).ToList();

        Assert.Equal(11, plan.Count);
        Assert.All(plan, a => Assert.False(a.IsPlaceholder, $"{a.SlotLabel} was blanked"));
        Assert.Equal("IDA", Assert.Single(plan, a => a.SlotLabel == "Castle Champion").Initials);
        Assert.Equal(14_614_120, Assert.Single(plan, a => a.SlotLabel == "Madness Champion").Value);
        Assert.Equal("KAS", Assert.Single(plan, a => a.SlotLabel == "King of the Realm #1").Initials);
        Assert.Equal("PTR", Assert.Single(plan, a => a.SlotLabel == "King of the Realm #4").Initials);
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
