using PinballScores.Core.Nvram;
using Xunit;

namespace PinballScores.Tests;

public class CategoryRulesTests
{
    [Theory]
    [InlineData("Gauntlet Champ 1", "GAUNTLET CHAMP")]
    [InlineData("Gauntlet Champ 3", "GAUNTLET CHAMP")]
    [InlineData("Officer's Club #1", "OFFICER'S CLUB")]
    [InlineData("Q Continuum #4", "Q CONTINUUM")]
    [InlineData("King of the Realm #2", "KING OF THE REALM")]
    [InlineData("Spider Champion", "SPIDER CHAMPION")]
    [InlineData("Best Bonus Champion", "BEST BONUS CHAMPION")]
    public void RankedSetsCollapseToOneCategory(string label, string expected) =>
        Assert.Equal(expected, CategoryRules.Normalise(label));

    [Fact]
    public void LabelThatIsOnlyANumberIsNotErasedEntirely() =>
        Assert.Equal("1", CategoryRules.Normalise("1"));

    [Theory]
    [InlineData("   ")]
    [InlineData("")]
    [InlineData("---")]
    [InlineData("...")]
    public void BlankSlotsAreNotRealHolders(string initials) =>
        Assert.True(CategoryRules.IsUnusedSlot(initials));

    [Theory]
    [InlineData("MHP")]
    [InlineData("G S")]
    [InlineData("A")]
    public void GenuineInitialsAreKept(string initials) =>
        Assert.False(CategoryRules.IsUnusedSlot(initials));
}
