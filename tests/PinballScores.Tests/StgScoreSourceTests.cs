using PinballScores.Core.Extraction;
using Xunit;

namespace PinballScores.Tests;

public class StgScoreSourceTests
{
    private static List<ExtractionResult> Read() =>
        new StgScoreSource(TestData.VpRegPath).Extract().ToList();

    [Fact]
    public void ReadsEveryTableStorageFromTheRealFile()
    {
        var results = Read();
        Assert.Contains(results, r => r.Table == "gotg_2020");
        Assert.Contains(results, r => r.Table == "leprechaun");
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
    public void NamedHighScoreVariablesBecomeTheirOwnCategory()
    {
        var gotg = Read().Single(r => r.Table == "gotg_2020");

        var combo = Assert.Single(gotg.Scores, s => s.Category == "COMBO");
        Assert.Equal("VPW", combo.Player);
        Assert.Equal(15, combo.Value);

        Assert.Contains(gotg.Scores, s => s.Category == "XANDAR");
        Assert.Contains(gotg.Scores, s => s.Category == "IMMO");
    }

    [Fact]
    public void NonScoreVariablesAreIgnored()
    {
        var gotg = Read().Single(r => r.Table == "gotg_2020");

        // Credits, ReplayValue and TotalGamesPlayed are not leaderboard entries.
        Assert.DoesNotContain(gotg.Scores, s => s.Value == 15 && s.Category is null);
        Assert.DoesNotContain(gotg.Scores, s => s.Category is "CREDITS" or "REPLAYVALUE" or "TOTALGAMESPLAYED");
    }

    [Fact]
    public void TableWithNoScoresYieldsNoEntriesRatherThanFailing()
    {
        var results = Read();
        var empty = results.SingleOrDefault(r => r.Table == "TAF_VPX_V2");

        Assert.NotNull(empty);
        Assert.Empty(empty.Scores);
    }

    [Fact]
    public void MissingFileIsNotAnError()
    {
        var results = new StgScoreSource("/nonexistent/VPReg.stg").Extract().ToList();
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
