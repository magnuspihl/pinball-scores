using PinballScores.Core;
using PinballScores.Core.Api;
using PinballScores.Core.Insertion;
using PinballScores.Core.Nvram;
using Xunit;

namespace PinballScores.Tests;

/// <summary>
/// Blanking a machine writes a blank-initials placeholder rather than clearing a record, because
/// a ROM restores its factory default over a record it considers invalid. Those
/// placeholders must never come back in as real scores, or blanking a board would
/// refill the API with its own filler.
/// </summary>
public class PlaceholderTests
{
    private static RemoteScore Score(string? category, string initials, long value) =>
        new() { Table = "smanve_101", Category = category, Initials = initials, Value = value.ToString() };

    [Theory]
    [InlineData(" ")]      // the current marker
    [InlineData("   ")]    // as it reads off a three-character field
    [InlineData("---")]    // the previous marker, still present in older records
    [InlineData(" --- ")]
    public void MarkerIsIgnoredWhenReadingBackOffTheMachine(string initials)
    {
        // The extractor drops these before they ever reach a submission batch.
        Assert.True(CategoryRules.IsUnusedSlot(initials));
    }

    [Fact]
    public void MarkerIsBlankBecauseWpcRejectsDashes()
    {
        // Williams WPC's boot-time validation only accepts initials from its own
        // selectable alphabet. "-" is not in it, and a record containing one is
        // reverted to the factory default; a space is accepted and survives.
        Assert.Equal(" ", Placeholder.Default.Initials);
        Assert.Equal(" ", new SyncOptions().PlaceholderMarker);
        Assert.DoesNotContain('-', Placeholder.Default.Initials);
    }

    [Fact]
    public void PlaceholderValueIsNonZero()
    {
        // A cleared record reads as invalid and the ROM replaces it with the
        // compiled-in factory default, so "blank" has to be a valid low score.
        Assert.True(Placeholder.Default.Value > 0);
        Assert.True(new SyncOptions().PlaceholderValue > 0);
    }

    [Fact]
    public void SlotsTheApiHasNoScoreForAreBlanked()
    {
        var map = TestData.Catalog.Find("smanve_101")!;

        // Only two real scores for a five-slot board.
        var plan = SlotPlanner.Plan(map, [Score(null, "AAA", 500), Score(null, "BBB", 900)])
            .Where(a => a.Category is null)
            .ToList();

        Assert.Equal(5, plan.Count);
        Assert.Equal("BBB", plan[0].Initials);
        Assert.Equal("AAA", plan[1].Initials);

        // Anything the API doesn't know about must not survive on the machine.
        foreach (var blanked in plan.Skip(2))
        {
            Assert.True(blanked.IsPlaceholder);
            Assert.Equal(" ", blanked.Initials);
            Assert.Equal(1, blanked.Value);
        }
    }

    [Fact]
    public void AnEmptyApiBoardBlanksTheWholeMachine()
    {
        var map = TestData.Catalog.Find("smanve_101")!;

        var plan = SlotPlanner.Plan(map, []);

        // Every slot, main board and named champions alike.
        Assert.NotEmpty(plan);
        Assert.All(plan, a => Assert.True(a.IsPlaceholder));
        Assert.Equal(map.HighScores.Count, plan.Count(a => a.Category is null));
    }

    [Fact]
    public void ACustomMarkerIsHonoured()
    {
        var map = TestData.Catalog.Find("smanve_101")!;

        var plan = SlotPlanner.Plan(map, [], new Placeholder("ZZZ", 7))
            .Where(a => a.Category is null)
            .ToList();

        Assert.All(plan, a => Assert.Equal("ZZZ", a.Initials));
        Assert.All(plan, a => Assert.Equal(7, a.Value));
    }

    [Fact]
    public void BlankedSlotsRoundTripToNothing()
    {
        // The full loop: blank a board, read it back, and confirm nothing would be
        // submitted — this is the failure mode that would refill a wiped API.
        var map = TestData.Catalog.Find("smanve_101")!;
        var blanked = SlotPlanner.Plan(map, []);

        var wouldSubmit = blanked
            .Where(a => !CategoryRules.IsUnusedSlot(a.Initials))
            .ToList();

        Assert.Empty(wouldSubmit);
    }
}
