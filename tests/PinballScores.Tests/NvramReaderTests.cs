using PinballScores.Core.Extraction;
using PinballScores.Core.Models;
using Xunit;

namespace PinballScores.Tests;

public class NvramReaderTests
{
    [Fact]
    public void ReadsSpiderManMainBoardFromRealCabinetBytes()
    {
        var scores = TestData.ReaderFor("smanve_101").ReadScores("smanve_101").ToList();
        var board = scores.Where(s => s.Category is null).ToList();

        Assert.Equal(5, board.Count);
        Assert.Equal(("SSR", 150_000_000L), (board[0].Player, board[0].Value));
        Assert.Equal(("LFS", 120_000_000L), (board[1].Player, board[1].Value));
        Assert.Equal(("KOK", 53_125_290L), (board[4].Player, board[4].Value));
    }

    [Fact]
    public void MainBoardSlotsCollapseToNullCategory()
    {
        // Grand Champion / First Place / ... are positional names for rank, not
        // separate boards, so none of them may leak through as a category.
        var scores = TestData.ReaderFor("smanve_101").ReadScores("smanve_101");

        Assert.DoesNotContain(scores, s =>
            s.Category is not null &&
            (s.Category.Contains("GRAND", StringComparison.Ordinal) ||
             s.Category.Contains("PLACE", StringComparison.Ordinal)));
    }

    [Fact]
    public void ReadsNamedAchievementBoards()
    {
        var scores = TestData.ReaderFor("smanve_101").ReadScores("smanve_101").ToList();

        var spider = Assert.Single(scores, s => s.Category == "SPIDER CHAMPION");
        Assert.Equal("DGH", spider.Player);
        Assert.Equal(25, spider.Value);
    }

    [Theory]
    [InlineData("smanve_101")]
    [InlineData("avs_170")]
    [InlineData("xmn_151h")]
    [InlineData("mm_109c")]
    [InlineData("taf_l7")]
    [InlineData("sttng_l7")]
    [InlineData("ij_l7")]
    [InlineData("t2_l8")]
    [InlineData("totan_14")]
    [InlineData("lotr")]
    [InlineData("btmn_106")]
    [InlineData("stwr_107")]
    [InlineData("simpprty")]
    public void EveryBundledMapMatchesItsSampleFile(string rom)
    {
        var reader = TestData.ReaderFor(rom);

        Assert.True(reader.ChecksumsValid(out var failure), $"{rom}: {failure}");
        Assert.NotEmpty(reader.ReadScores(rom));
    }

    [Fact]
    public void MainBoardIsStoredInDescendingOrder()
    {
        // The machine keeps one sorted array and re-sorts records between slots,
        // which is the basis for deriving rank instead of storing it.
        foreach (var rom in new[] { "smanve_101", "mm_109c", "sttng_l7", "taf_l7", "btmn_106" })
        {
            var board = TestData.ReaderFor(rom).ReadScores(rom)
                .Where(s => s.Category is null)
                .Select(s => s.Value)
                .ToList();

            Assert.Equal(board.OrderByDescending(v => v).ToList(), board);
        }
    }

    [Fact]
    public void CorruptedRecordIsRejectedRatherThanPublished()
    {
        // A map that nearly matches decodes to plausible garbage instead of failing,
        // so the checksum is the only thing standing between us and invented scores.
        var data = TestData.Nvram("smanve_101");
        data[0x2b80] ^= 0xFF; // flip a byte inside the Grand Champion record

        Assert.False(TestData.ReaderFor("smanve_101", data).ChecksumsValid(out var failure));
        Assert.Contains("checksum16", failure);
    }

    [Fact]
    public void ScoresAreExactBeyondFloatPrecision()
    {
        // The old CLI stored scores as float32, which silently rounded anything above
        // ~16.7M: a real 738,778,270 was recorded as 738,778,240.
        var second = TestData.ReaderFor("sttng_l7").ReadScores("sttng_l7")
            .Where(s => s.Category is null)
            .Skip(2)
            .First();

        Assert.Equal(738_778_270L, second.Value);
        Assert.NotEqual(738_778_240L, second.Value);
    }

    [Fact]
    public void CountersAreTypedSeparatelyFromScores()
    {
        // "6 Castles Destroyed" is a count of things, not a points total. The map
        // carries that as structured metadata, so no per-table string hacks.
        var castle = TestData.ReaderFor("mm_109c").ReadScores("mm_109c")
            .Single(s => s.Category == "CASTLE CHAMPION");

        Assert.Equal(ScoreValueKind.Counter, castle.ValueKind);
        Assert.Equal("Castles Destroyed", castle.DisplaySuffix);
    }

    [Fact]
    public void RankedNamedSetsCollapseToOneCategory()
    {
        // Officer's Club #1..#4 is one board with four slots, matching the agreed
        // treatment of Gauntlet Champ 1/2/3.
        var officers = TestData.ReaderFor("sttng_l7").ReadScores("sttng_l7")
            .Where(s => s.Category == "OFFICER'S CLUB")
            .ToList();

        Assert.Equal(4, officers.Count);
    }

    [Fact]
    public void PlaceholderFileIsSkippedWithAReasonRatherThanCrashing()
    {
        var results = new NvramScoreSource(TestData.NvramDirectory, TestData.Catalog).Extract().ToList();

        // The 3-byte spagb_100 file is not a machine and must not reach the API.
        var placeholder = Assert.Single(results, r => r.Table == "spagb_100");
        Assert.NotNull(placeholder.Skipped);
        Assert.Empty(placeholder.Scores);
    }

    [Fact]
    public void ExtractsEveryMappedTableInOnePass()
    {
        var results = new NvramScoreSource(TestData.NvramDirectory, TestData.Catalog).Extract().ToList();
        var extracted = results.Where(r => r.Skipped is null).ToList();

        // Every sample .nv except the placeholder now has a bundled map.
        Assert.Equal(15, extracted.Count);
        Assert.Single(results, r => r.Skipped is not null);
        Assert.True(extracted.Sum(r => r.Scores.Count) > 100);
    }

    [Fact]
    public void PiratesOfTheCaribbeanMatchesThePreviousImplementationsOutput()
    {
        // These exact values were captured as PINemHi console output in the old
        // project's unit test, so they cross-check a map built without PINemHi.
        var scores = TestData.ReaderFor("potc_600as").ReadScores("potc_600as").ToList();
        var board = scores.Where(s => s.Category is null).ToList();

        Assert.Equal(("MHP", 128_591_800L), (board[0].Player, board[0].Value));
        Assert.Equal(("MHP", 100_609_520L), (board[1].Player, board[1].Value));
        Assert.Equal(("KHP", 81_263_930L), (board[2].Player, board[2].Value));

        var pirateKing = Assert.Single(scores, s => s.Category == "PIRATE KING");
        Assert.Equal(("KEF", 25L), (pirateKing.Player, pirateKing.Value));

        // Gauntlet Champ 1/2/3 collapse to one category, ranked by value.
        var gauntlet = scores.Where(s => s.Category == "GAUNTLET CHAMP").ToList();
        Assert.Equal(3, gauntlet.Count);
        Assert.Equal([15L, 10L, 5L], gauntlet.Select(s => s.Value));
    }

    [Fact]
    public void WalkingDeadMatchesTheRealCabinetsPublishedScores()
    {
        // Cross-checked against what the previous implementation published to the API
        // from the physical machine.
        var scores = TestData.ReaderFor("twd_156h").ReadScores("twd_156h").ToList();

        Assert.Equal(19, scores.Count);
        Assert.Equal(5, scores.Count(s => s.Category is null));

        foreach (var (category, player, value) in new[]
                 {
                     ("WALKERS KILLED CHAMPION", "DAV", 25L),
                     ("COMBO CHAMPION", "MAR", 2_500_000L),
                     ("LAST MAN STANDING CHAMPION", "EB", 75_000_000L),
                     ("HORDE CHAMPION", "L E", 50_000_000L),
                 })
        {
            var entry = Assert.Single(scores, s => s.Category == category);
            Assert.Equal((player, value), (entry.Player, entry.Value));
        }
    }

    [Theory]
    [InlineData("potc_600as")]
    [InlineData("twd_156h")]
    public void NewlyWrittenMapsValidateAgainstTheirSampleFiles(string rom)
    {
        Assert.True(TestData.ReaderFor(rom).ChecksumsValid(out var failure), $"{rom}: {failure}");
    }

    [Theory]
    [InlineData("potc_600as", "PIRATE KING", "KEF", 25L)]
    [InlineData("potc_600as", "GAUNTLET CHAMP", "J B", 15L)]
    [InlineData("potc_600as", "DAVY JONES CHAMPION", "XAQ", 5L)]
    [InlineData("twd_156h", "WALKERS KILLED CHAMPION", "DAV", 25L)]
    [InlineData("xmn_151h", "COMBO CHAMPION", "YAN", 15L)]
    public void AchievementTalliesAreTypedAsCountersNotScores(string rom, string category, string player, long value)
    {
        // These fields count things — pirates gauntleted, walkers killed — and the
        // maps say so with a "counter" descriptor rather than a "score" one. Sending
        // the right value_type is what lets the site render them sensibly.
        var entry = TestData.ReaderFor(rom).ReadScores(rom).Single(s => s.Category == category && s.Player == player);

        Assert.Equal(ScoreValueKind.Counter, entry.ValueKind);
        Assert.Equal(value, entry.Value);
    }
}
