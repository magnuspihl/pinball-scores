using PinballScores.Core.Extraction;
using PinballScores.Core.Models;
using Xunit;

namespace PinballScores.Tests;

public class StgScoreSourceTests
{
    private static List<ExtractionResult> Read() =>
        new StgScoreSource(TestData.VpRegPath, TestData.Catalog).Extract().ToList();

    [Fact]
    public void ReadsTheMappedTables()
    {
        var results = Read();

        foreach (var table in new[] { "gotg_2020", "jpsdeadpool", "gameofthrones" })
            Assert.Contains(results, r => r.Table == table && r.Skipped is null);
    }

    [Fact]
    public void UnmappedStoragesAreNotTouched()
    {
        // VPReg.stg is shared and accumulates tables the cabinet does not track.
        // They are not reset when the cabinet is blanked, so reading them would feed
        // stale scores into a freshly wiped database.
        var results = Read();

        Assert.DoesNotContain(results, r => r.Table == "leprechaun");
        Assert.DoesNotContain(results, r => r.Table == "TAF_VPX_V2");
    }

    [Fact]
    public void NumberedHighScoresAreTheMainBoard()
    {
        var gotg = Read().Single(r => r.Table == "gotg_2020");
        var board = gotg.Scores.Where(s => s.Category is null).ToList();

        Assert.Equal(5, board.Count);
        Assert.Contains(board, s => s is { Player: "MPT", Value: 75_000_000 });
        Assert.Contains(board, s => s is { Player: "DPF", Value: 83_986_330 });
    }

    [Fact]
    public void ChampionFieldsUseTheMapsCategoryKeys()
    {
        var gotg = Read().Single(r => r.Table == "gotg_2020");

        var combo = Assert.Single(gotg.Scores, s => s.Category == "combo");
        Assert.Equal("VPW", combo.Player);
        Assert.Equal(15, combo.Value);

        Assert.Contains(gotg.Scores, s => s.Category == "xandar");
        Assert.Contains(gotg.Scores, s => s.Category == "immo");
        Assert.Contains(gotg.Scores, s => s.Category == "cb");
    }

    [Fact]
    public void ComboCountIsACounterWhileTheOthersAreScores()
    {
        // A combo tally of 15 sitting beside three 25,000,000 point totals is a
        // count, not a score — and Spider-Man's combo_champion is typed the same way.
        var gotg = Read().Single(r => r.Table == "gotg_2020");

        Assert.Equal(ScoreValueKind.Counter, gotg.Scores.Single(s => s.Category == "combo").ValueKind);
        Assert.Equal(ScoreValueKind.Score, gotg.Scores.Single(s => s.Category == "cb").ValueKind);
    }

    [Fact]
    public void NonScoreVariablesAreIgnored()
    {
        var gotg = Read().Single(r => r.Table == "gotg_2020");

        // Credits, ReplayValue and TotalGamesPlayed are not declared in the map, so
        // they cannot be mistaken for scores.
        Assert.DoesNotContain(gotg.Scores, s => s.Category is "credits" or "replayvalue" or "totalgamesplayed");
    }

    [Fact]
    public void MissingFileIsNotAnError()
    {
        var results = new StgScoreSource("/nonexistent/VPReg.stg", TestData.Catalog).Extract().ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void VisualPinballBoardsNeedNotBeStoredInOrder()
    {
        // Unlike NVRAM, VPX slot order is not rank order — HighScore5 here beats
        // HighScore1. Rank is derived from the value, so this is harmless, but it
        // is why we must never treat the slot index as a rank.
        var board = Read().Single(r => r.Table == "gotg_2020").Scores
            .Where(s => s.Category is null)
            .Select(s => s.Value)
            .ToList();

        Assert.NotEqual(board.OrderByDescending(v => v).ToList(), board);
    }
}
