using PinballScores.Core.Api;
using PinballScores.Core.Extraction;
using PinballScores.Core.Insertion;
using PinballScores.Core.Nvram;
using Xunit;

namespace PinballScores.Tests;

/// <summary>
/// Write-back against the real committed cabinet files. Every test copies the
/// sample first — the originals are the read-side fixtures and must not move.
/// </summary>
public class WriteBackTests
{
    private static RemoteScore Score(string? category, string initials, long value) =>
        new() { Category = category, Initials = initials, Value = value.ToString() };

    /// <summary>A coronation as the API holds it: who, which completion, when, and how often.</summary>
    private static RemoteScore King(string initials, long completion, string crownedAt, int crownedCount) =>
        new()
        {
            Category = "king_of_the_realm",
            Initials = initials,
            Value = completion.ToString(),
            Metadata = new Dictionary<string, string>
            {
                ["crowned_at"] = crownedAt,
                ["crowned_count"] = crownedCount.ToString(),
            },
        };

    private static string CopyNvram(string rom)
    {
        var dir = Directory.CreateTempSubdirectory("wb-nv-").FullName;
        var path = Path.Combine(dir, rom + ".nv");
        File.WriteAllBytes(path, TestData.Nvram(rom));
        return dir;
    }

    private static string CopyVpReg()
    {
        var dir = Directory.CreateTempSubdirectory("wb-stg-").FullName;
        var path = Path.Combine(dir, "VPReg.stg");
        File.Copy(TestData.VpRegPath, path);
        return path;
    }

    // ---------- NVRAM ----------

    [Fact]
    public async Task WritingAScoreMakesItReadBack()
    {
        var dir = CopyNvram("smanve_101");
        var writer = new NvramScoreWriter(TestData.Catalog, dir);

        var result = await writer.WriteAsync("smanve_101", [Score(null, "ZZZ", 123_456_780)]);

        Assert.True(result.Applied, result.Skipped);

        var read = new NvramScoreSource(dir, TestData.Catalog).Extract().Single();
        Assert.Contains(read.Scores, s => s is { Player: "ZZZ", Value: 123_456_780, Category: null });
    }

    [Fact]
    public async Task ChecksumsAreRecomputedSoTheRomWouldAcceptTheRecord()
    {
        // The whole reason earlier attempts reverted: a record whose integrity tag
        // does not match is replaced with the ROM's factory default on boot.
        var dir = CopyNvram("smanve_101");
        await new NvramScoreWriter(TestData.Catalog, dir).WriteAsync("smanve_101", [Score(null, "ABC", 99_000_000)]);

        var data = File.ReadAllBytes(Path.Combine(dir, "smanve_101.nv"));
        var map = TestData.Catalog.Find("smanve_101")!;
        var reader = new NvramReader(data, map, TestData.Catalog.PlatformFor(map));

        Assert.True(reader.ChecksumsValid(out var failure), failure);
    }

    [Fact]
    public async Task ChecksumsAreRecomputedOnWpcToo()
    {
        // WPC uses checksum8 with groupings and checksum16 regions, big endian —
        // a different code path from Stern SAM's per-record tags.
        var dir = CopyNvram("mm_109c");
        var result = await new NvramScoreWriter(TestData.Catalog, dir)
            .WriteAsync("mm_109c", [Score(null, "WPC", 91_000_000)]);

        Assert.True(result.Applied, result.Skipped);

        var data = File.ReadAllBytes(Path.Combine(dir, "mm_109c.nv"));
        var map = TestData.Catalog.Find("mm_109c")!;
        Assert.True(new NvramReader(data, map, TestData.Catalog.PlatformFor(map)).ChecksumsValid(out var f), f);
    }

    [Fact]
    public async Task AnEmptyBoardBlanksEverySlot()
    {
        var dir = CopyNvram("smanve_101");

        var result = await new NvramScoreWriter(TestData.Catalog, dir).WriteAsync("smanve_101", []);

        Assert.True(result.Applied, result.Skipped);

        // Blank initials read as an unused slot, so a blanked machine yields nothing.
        var read = new NvramScoreSource(dir, TestData.Catalog).Extract().Single();
        Assert.Empty(read.Scores);
    }

    [Fact]
    public async Task WritingTwiceIsANoOpTheSecondTime()
    {
        var dir = CopyNvram("smanve_101");
        var writer = new NvramScoreWriter(TestData.Catalog, dir);
        var board = new[] { Score(null, "AAA", 50_000_000) };

        Assert.True((await writer.WriteAsync("smanve_101", board)).Applied);

        var second = await writer.WriteAsync("smanve_101", board);
        Assert.False(second.Applied);
        Assert.Equal("already up to date", second.Skipped);
    }

    [Fact]
    public async Task OnlyTheTargetedBytesChange()
    {
        // A write that disturbed unrelated regions could corrupt audits or settings.
        var dir = CopyNvram("smanve_101");
        var before = TestData.Nvram("smanve_101");

        await new NvramScoreWriter(TestData.Catalog, dir).WriteAsync("smanve_101", [Score(null, "AAA", 50_000_000)]);
        var after = File.ReadAllBytes(Path.Combine(dir, "smanve_101.nv"));

        Assert.Equal(before.Length, after.Length);

        // Every changed byte must fall inside a mapped record or its checksum.
        var map = TestData.Catalog.Find("smanve_101")!;
        var platform = TestData.Catalog.PlatformFor(map);
        var allowed = new HashSet<int>();
        foreach (var slot in map.HighScores.Concat(map.ModeChampions))
        {
            foreach (var descriptor in slot.Fields.Values.Append(slot.Initials))
            {
                if (descriptor is null) continue;
                foreach (var address in descriptor.Addresses())
                    allowed.Add((int)(address - platform.NvramBaseAddress));
            }
        }
        foreach (var span in NvramChecksum.Spans(map, platform, after.Length))
            for (var i = 0; i < span.Width; i++) allowed.Add(span.ChecksumOffset + i);

        for (var i = 0; i < after.Length; i++)
            if (before[i] != after[i])
                Assert.True(allowed.Contains(i), $"byte 0x{i:X} changed but is not part of any mapped record");
    }

    [Fact]
    public async Task StarWarsIsRefusedBecauseItsShadowCopyUndoesTheWrite()
    {
        // stwr_107's boot code restores the whole table from an undocumented shadow
        // when rank 1 looks too low. Writing would revert confusingly, so refuse.
        var dir = CopyNvram("stwr_107");

        var result = await new NvramScoreWriter(TestData.Catalog, dir).WriteAsync("stwr_107", []);

        Assert.False(result.Applied);
        Assert.Contains("shadow", result.Skipped);
    }

    [Theory]
    [InlineData("m-p", "M P")]
    [InlineData("a.b", "A B")]
    [InlineData("---", "   ")]
    [InlineData("abcdef", "ABC")]
    public void InitialsAreCoercedIntoTheMachinesAlphabet(string input, string expected)
    {
        // WPC reverts any record containing a character it cannot itself display.
        Assert.Equal(expected, NvramWriter.SanitiseInitials(input, 3));
    }

    [Fact]
    public async Task AValueTooLargeForTheFieldIsRefusedRatherThanTruncated()
    {
        // Silently wrapping would put a wrong score on the machine and then feed it
        // back to the API as if it were real.
        var dir = CopyNvram("mm_109c");

        var result = await new NvramScoreWriter(TestData.Catalog, dir)
            .WriteAsync("mm_109c", [Score(null, "AAA", 999_999_999_999)]);

        Assert.Contains(result.Planned, line => line.StartsWith("main left untouched:"));

        var read = new NvramScoreSource(dir, TestData.Catalog).Extract().Single();
        Assert.DoesNotContain(read.Scores, s => s.Player == "AAA");
    }

    [Fact]
    public async Task AFailedCategoryLeavesItsOwnSlotsUntouched()
    {
        // One unwritable value used to fail the whole machine. It now costs that
        // category and nothing else — the rest of the board is still applied, and
        // the failed category keeps the bytes it already had.
        var dir = CopyNvram("mm_109c");
        var before = new NvramScoreSource(dir, TestData.Catalog).Extract().Single()
            .Scores.Where(s => s.Category is null).ToList();

        var result = await new NvramScoreWriter(TestData.Catalog, dir).WriteAsync("mm_109c",
        [
            Score(null, "AAA", 999_999_999_999),
            Score("madness_champion", "BBB", 30_000_000),
        ]);

        Assert.True(result.Applied, result.Skipped);

        var after = new NvramScoreSource(dir, TestData.Catalog).Extract().Single();
        Assert.Equal(before, after.Scores.Where(s => s.Category is null).ToList());
        Assert.Contains(after.Scores, s => s is { Player: "BBB", Value: 30_000_000, Category: "madness_champion" });
    }

    // ---------- Visual Pinball ----------

    [Fact]
    public async Task WritingAVpxScoreMakesItReadBack()
    {
        var path = CopyVpReg();
        var writer = new StgScoreWriter(path, TestData.Catalog);

        var result = await writer.WriteAsync("gotg_2020", [Score(null, "QQQ", 88_000_000)]);

        Assert.True(result.Applied, result.Skipped);

        var read = new StgScoreSource(path, TestData.Catalog).Extract().Single(r => r.Table == "gotg_2020");
        Assert.Contains(read.Scores, s => s is { Player: "QQQ", Value: 88_000_000, Category: null });
    }

    [Fact]
    public async Task VpxValuesAreNotConstrainedToTheOriginalDigitCount()
    {
        // The Python reference could only do same-length replacement; a proper CFB
        // writer resizes the stream, so no zero-padding is needed.
        var path = CopyVpReg();

        await new StgScoreWriter(path, TestData.Catalog).WriteAsync("gotg_2020", [Score(null, "AAA", 7)]);

        var read = new StgScoreSource(path, TestData.Catalog).Extract().Single(r => r.Table == "gotg_2020");
        Assert.Contains(read.Scores, s => s is { Player: "AAA", Value: 7 });
    }

    [Fact]
    public async Task WritingOneVpxTableLeavesTheOthersAlone()
    {
        // VPReg.stg is shared by every table; a careless write would take them all.
        var path = CopyVpReg();
        var before = new StgScoreSource(path, TestData.Catalog).Extract()
            .Single(r => r.Table == "jpsdeadpool").Scores.ToList();

        await new StgScoreWriter(path, TestData.Catalog).WriteAsync("gotg_2020", [Score(null, "AAA", 1_000)]);

        var after = new StgScoreSource(path, TestData.Catalog).Extract()
            .Single(r => r.Table == "jpsdeadpool").Scores.ToList();

        Assert.Equal(before, after);
    }

    [Fact]
    public async Task NamedVpxCategoriesGoToTheirOwnSlots()
    {
        var path = CopyVpReg();

        await new StgScoreWriter(path, TestData.Catalog)
            .WriteAsync("gotg_2020", [Score("combo", "CMB", 42)]);

        var read = new StgScoreSource(path, TestData.Catalog).Extract().Single(r => r.Table == "gotg_2020");
        var combo = Assert.Single(read.Scores, s => s.Category == "combo");
        Assert.Equal(("CMB", 42L), (combo.Player, combo.Value));
    }

    [Fact]
    public async Task AnEmptyBoardBlanksTheVpxTable()
    {
        var path = CopyVpReg();

        var result = await new StgScoreWriter(path, TestData.Catalog).WriteAsync("gotg_2020", []);

        Assert.True(result.Applied, result.Skipped);
        var read = new StgScoreSource(path, TestData.Catalog).Extract().Single(r => r.Table == "gotg_2020");
        Assert.Empty(read.Scores);
    }

    [Fact]
    public async Task AnUnmappedVpxTableIsRefused()
    {
        var path = CopyVpReg();

        var result = await new StgScoreWriter(path, TestData.Catalog).WriteAsync("leprechaun", []);

        Assert.False(result.Applied);
        Assert.Contains("no bundled STG map", result.Skipped);
    }


    // ---------- dry run must never touch a byte ----------

    [Fact]
    public async Task DryRunDoesNotModifyNvram()
    {
        // --plan promises it touches nothing, and it is the recommended way to check
        // a live cabinet before enabling write-back.
        var dir = CopyNvram("smanve_101");
        var path = Path.Combine(dir, "smanve_101.nv");
        var before = File.ReadAllBytes(path);

        var result = await new NvramScoreWriter(TestData.Catalog, dir)
            .WriteAsync("smanve_101", [Score(null, "ZZZ", 123_456_780)], dryRun: true);

        Assert.False(result.Applied);
        Assert.Equal("dry run", result.Skipped);
        Assert.NotEmpty(result.Planned);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task DryRunDoesNotModifyVpReg()
    {
        var path = CopyVpReg();
        var before = File.ReadAllBytes(path);

        var result = await new StgScoreWriter(path, TestData.Catalog)
            .WriteAsync("gotg_2020", [Score(null, "QQQ", 88_000_000)], dryRun: true);

        Assert.False(result.Applied);
        Assert.Equal("dry run", result.Skipped);
        Assert.NotEmpty(result.Planned);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task DryRunStillReportsWhatItWouldHaveWritten()
    {
        var dir = CopyNvram("smanve_101");

        var result = await new NvramScoreWriter(TestData.Catalog, dir)
            .WriteAsync("smanve_101", [Score(null, "ZZZ", 123_456_780)], dryRun: true);

        Assert.Contains(result.Planned, line => line.Contains("ZZZ") && line.Contains("123456780"));
    }

    // ---------- partial boards (competition start) ----------

    [Fact]
    public async Task PartialBoardsBlankTheRemainingSlots()
    {
        // A competition starts with an empty or near-empty board and the cabinet must
        // reflect that, not keep pre-competition scores in the unfilled slots.
        var dir = CopyNvram("smanve_101");

        var result = await new NvramScoreWriter(TestData.Catalog, dir).WriteAsync("smanve_101",
        [
            Score(null, "AAA", 5_000_000),
            Score(null, "BBB", 9_000_000),
        ]);

        Assert.True(result.Applied, result.Skipped);

        // Only the two survive; the other three slots read as unused.
        var read = new NvramScoreSource(dir, TestData.Catalog).Extract().Single();
        var board = read.Scores.Where(s => s.Category is null).ToList();
        Assert.Equal(2, board.Count);
        Assert.Equal(("BBB", 9_000_000L), (board[0].Player, board[0].Value));
        Assert.Equal(("AAA", 5_000_000L), (board[1].Player, board[1].Value));
    }

    [Fact]
    public async Task BlankedSlotsHoldTheMarkerAndValueOneOnDisk()
    {
        // Not merely "reads as empty": the bytes must be a valid low record, because
        // a cleared record is invalid and the ROM restores its factory default.
        var dir = CopyNvram("mm_109c");
        await new NvramScoreWriter(TestData.Catalog, dir)
            .WriteAsync("mm_109c", [Score(null, "AAA", 5_000_000)]);

        var map = TestData.Catalog.Find("mm_109c")!;
        var platform = TestData.Catalog.PlatformFor(map);
        var reader = new NvramReader(File.ReadAllBytes(Path.Combine(dir, "mm_109c.nv")), map, platform);

        // Second place onwards: three spaces on WPC's fixed-width field, value 1.
        var second = map.HighScores[1];
        Assert.Equal("   ", reader.ReadChars(second.Initials!));
        Assert.Equal(1, reader.ReadValue(second.Value!));
    }

    [Fact]
    public async Task ACoronationIsWrittenWholeOrNotAtAll()
    {
        // The bug this fixes: only initials and the counter were written, so a king
        // arrived on the cabinet wearing the previous king's date and ordinal — the
        // factory-seeded 2022-12-31 21:32 that every untouched machine ships with.
        var dir = CopyNvram("mm_109c");

        var result = await new NvramScoreWriter(TestData.Catalog, dir).WriteAsync("mm_109c",
        [
            King("KAS", 16, "2026-06-14 20:41", 2),
            King("VIB", 14, "2026-03-02 17:08", 1),
        ]);

        Assert.True(result.Applied, result.Skipped);

        var kings = new NvramScoreSource(dir, TestData.Catalog).Extract().Single()
            .Scores.Where(s => s.Category == "king_of_the_realm").ToList();

        Assert.Equal(2, kings.Count);
        Assert.Equal(("KAS", 16L), (kings[0].Player, kings[0].Value));
        Assert.Equal("2026-06-14 20:41", kings[0].Metadata!["crowned_at"]);
        Assert.Equal("2", kings[0].Metadata!["crowned_count"]);
        Assert.Equal("2026-03-02 17:08", kings[1].Metadata!["crowned_at"]);
    }

    [Fact]
    public async Task AWrittenClockKeepsTheWeekdayTheMachineWouldStore()
    {
        // The weekday is stored, not derived, and WPC counts Sunday=1..Saturday=7.
        // Get it wrong and the DMD names the wrong day beside a correct date.
        var dir = CopyNvram("mm_109c");
        await new NvramScoreWriter(TestData.Catalog, dir)
            .WriteAsync("mm_109c", [King("KAS", 16, "2026-06-14 20:41", 1)]);

        var map = TestData.Catalog.Find("mm_109c")!;
        var platform = TestData.Catalog.PlatformFor(map);
        var data = File.ReadAllBytes(Path.Combine(dir, "mm_109c.nv"));
        var clock = map.ModeChampions.Single(s => s.Label == "King of the Realm #1").Field("timestamp")!;
        var bytes = clock.Addresses().Select(a => data[(int)(a - platform.NvramBaseAddress)]).ToArray();

        // 2026-06-14 is a Sunday.
        Assert.Equal(new byte[] { 0x07, 0xEA, 6, 14, 1, 20, 41 }, bytes);
    }

    [Fact]
    public async Task ABlankedCoronationIsZeroedTheWayTheMachineDoesIt()
    {
        // A log's empty slot is zero — that is what an untouched Medieval Madness
        // holds in kings #2-#4 — not the low-but-nonzero marker a ranked board needs
        // to stop the ROM restoring its factory default.
        var dir = CopyNvram("mm_109c");
        await new NvramScoreWriter(TestData.Catalog, dir)
            .WriteAsync("mm_109c", [King("KAS", 16, "2026-06-14 20:41", 1)]);

        var map = TestData.Catalog.Find("mm_109c")!;
        var reader = new NvramReader(File.ReadAllBytes(Path.Combine(dir, "mm_109c.nv")), map,
            TestData.Catalog.PlatformFor(map));
        var empty = map.ModeChampions.Single(s => s.Label == "King of the Realm #2");

        Assert.Equal("   ", reader.ReadChars(empty.Initials!));
        Assert.Equal(0, reader.ReadValue(empty.Field("counter")!));
        Assert.Equal(0, reader.ReadValue(empty.Field("nth time")!));
    }

    [Fact]
    public async Task PreCompetitionScoresDoNotSurviveInUnfilledSlots()
    {
        // The failure that would matter: a table keeps its old leaderboard below the
        // competition scores, so the cabinet shows a mix of both.
        var dir = CopyNvram("smanve_101");

        // Start from a full board.
        await new NvramScoreWriter(TestData.Catalog, dir).WriteAsync("smanve_101",
        [
            Score(null, "OLD", 90_000_000), Score(null, "OLE", 80_000_000),
            Score(null, "OLF", 70_000_000), Score(null, "OLG", 60_000_000),
            Score(null, "OLH", 50_000_000),
        ]);

        // Competition begins: the API now returns a single score.
        await new NvramScoreWriter(TestData.Catalog, dir)
            .WriteAsync("smanve_101", [Score(null, "NEW", 1_000_000)]);

        var read = new NvramScoreSource(dir, TestData.Catalog).Extract().Single();
        Assert.Single(read.Scores, s => s.Category is null);
        Assert.DoesNotContain(read.Scores, s => s.Player.StartsWith("OL"));
    }

    [Fact]
    public async Task NamedCategoriesAreBlankedIndependently()
    {
        // A competition on the main board must not leave stale champions either.
        var dir = CopyNvram("smanve_101");

        await new NvramScoreWriter(TestData.Catalog, dir).WriteAsync("smanve_101",
            [Score("spider_champion", "SPD", 50)]);
        Assert.Single(new NvramScoreSource(dir, TestData.Catalog).Extract().Single().Scores);

        await new NvramScoreWriter(TestData.Catalog, dir).WriteAsync("smanve_101", []);
        Assert.Empty(new NvramScoreSource(dir, TestData.Catalog).Extract().Single().Scores);
    }

    [Fact]
    public async Task PartialVpxBoardsBlankTheRemainingSlots()
    {
        var path = CopyVpReg();

        await new StgScoreWriter(path, TestData.Catalog)
            .WriteAsync("gotg_2020", [Score(null, "AAA", 1_000), Score(null, "BBB", 2_000)]);

        var read = new StgScoreSource(path, TestData.Catalog).Extract().Single(r => r.Table == "gotg_2020");
        Assert.Equal(2, read.Scores.Count(s => s.Category is null));
        Assert.DoesNotContain(read.Scores, s => s.Category is not null);
    }
}
