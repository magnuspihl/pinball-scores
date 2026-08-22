using PinballScores.Core.Models;
using PinballScores.Core.Nvram;
using Xunit;

namespace PinballScores.Tests;

public class CategoryRulesTests
{
    [Theory]
    [InlineData("   ")]
    [InlineData("")]
    [InlineData(" ")]
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

    [Theory]
    [InlineData("Spider Champion", "spider_champion")]
    [InlineData("Officer's Club #1", "officer_s_club_1")]
    [InlineData("CDC Champion", "cdc_champion")]
    [InlineData("  ", "unknown")]
    public void SlugFallbackMatchesTheShapeMapsUse(string label, string expected) =>
        Assert.Equal(expected, CategoryRules.Slugify(label));
}

/// <summary>
/// Categories come from the map's own <c>_pinballscores.categories</c> block rather
/// than from parsing slot labels, so the CLI and the maps cannot drift apart.
/// </summary>
public class CategoryDefinitionTests
{
    [Fact]
    public void MainBoardIsSubmittedAsNull()
    {
        var map = TestData.Catalog.Find("smanve_101")!;
        var main = Assert.Single(map.Categories, c => c.IsMainBoard);

        Assert.Equal("main", main.Key);
        Assert.Null(main.ApiCategory);
        Assert.Equal(5, main.Slots.Count);
    }

    [Fact]
    public void NamedCategoriesAreSubmittedAsTheirKey()
    {
        // The key is stable; the label is the website's to change. Submitting the
        // label would mean a rename splits one category into two.
        var map = TestData.Catalog.Find("smanve_101")!;
        var spider = Assert.Single(map.Categories, c => c.Key == "spider_champion");

        Assert.Equal("spider_champion", spider.ApiCategory);
        Assert.Equal("Spider Champion", spider.Name);
    }

    [Fact]
    public void ValueTypeComesFromTheMap()
    {
        var map = TestData.Catalog.Find("smanve_101")!;

        Assert.Equal(ScoreValueKind.Counter, map.Categories.Single(c => c.Key == "spider_champion").ValueKind);
        Assert.Equal(ScoreValueKind.Score, map.Categories.Single(c => c.IsMainBoard).ValueKind);
    }

    [Theory]
    // The API's own spelling: it keys a category by the string first submitted for
    // it, and the rows loaded before this CLI existed carry the ROM's upper-case
    // label rather than the map's key.
    [InlineData("SPIDER CHAMPION")]
    [InlineData("Spider Champion")]
    [InlineData("spider_champion")]
    [InlineData("SPIDER_CHAMPION")]
    public void ApiSpellingsOfACategoryAllResolveToIt(string apiCategory)
    {
        var spider = TestData.Catalog.Find("smanve_101")!.Categories.Single(c => c.Key == "spider_champion");

        Assert.True(spider.Matches(apiCategory));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("BEST BONUS CHAMPION")]
    public void ACategoryDoesNotClaimAnotherBoardsRows(string? apiCategory)
    {
        var spider = TestData.Catalog.Find("smanve_101")!.Categories.Single(c => c.Key == "spider_champion");

        Assert.False(spider.Matches(apiCategory));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void TheMainBoardTakesTheRowsWithNoCategory(string? apiCategory)
    {
        var map = TestData.Catalog.Find("smanve_101")!;

        Assert.True(map.Categories.Single(c => c.IsMainBoard).Matches(apiCategory));
    }

    [Fact]
    public void TheMainBoardTakesOnlyThoseRows()
    {
        var map = TestData.Catalog.Find("smanve_101")!;

        Assert.False(map.Categories.Single(c => c.IsMainBoard).Matches("SPIDER CHAMPION"));
    }

    [Fact]
    public void NoTwoCategoriesInAMapAreToldApartOnlyByPunctuation()
    {
        // Matching ignores case and separators, so two categories that differ only
        // in those would both claim the same rows.
        foreach (var rom in TestData.Catalog.KnownRoms)
        {
            var map = TestData.Catalog.Find(rom)!;

            foreach (var category in map.Categories.Where(c => !c.IsMainBoard))
                Assert.Single(map.Categories, c => c.Matches(category.ApiCategory));
        }
    }

    [Fact]
    public void EveryBundledMapPlacesEverySlotInACategory()
    {
        // The slug fallback exists so a missed slot is not lost, but no bundled map
        // should need it.
        foreach (var rom in TestData.Catalog.KnownRoms)
        {
            var map = TestData.Catalog.Find(rom)!;
            Assert.NotEmpty(map.Categories);

            foreach (var slot in map.HighScores.Concat(map.ModeChampions))
                Assert.True(map.CategoryForSlot(slot.Label) is not null,
                    $"{rom}: slot '{slot.Label}' is in no category");
        }
    }

    [Fact]
    public void EveryBundledMapHasExactlyOneMainBoard()
    {
        foreach (var rom in TestData.Catalog.KnownRoms)
            Assert.Single(TestData.Catalog.Find(rom)!.Categories, c => c.IsMainBoard);

        foreach (var stg in TestData.Catalog.StgMaps)
            Assert.Single(stg.Categories, c => c.IsMainBoard);
    }
}
