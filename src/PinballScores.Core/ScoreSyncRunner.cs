using Microsoft.Extensions.Logging;
using PinballScores.Core.Api;
using PinballScores.Core.Extraction;
using PinballScores.Core.Insertion;
using PinballScores.Core.Models;
using PinballScores.Core.Nvram;

namespace PinballScores.Core;

/// <summary>Totals for one run, for logging and for tests.</summary>
public sealed record SyncReport(
    int TablesRead,
    int TablesSkipped,
    int ScoresFound,
    int Inserted,
    int Duplicates,
    int Rejected,
    int TablesWritten);

/// <summary>
/// One complete pass: read every machine, submit what was found, then write the
/// API's authoritative board back. Submission happens first so a score set since
/// the last run is banked before anything overwrites the machine.
/// </summary>
public sealed class ScoreSyncRunner
{
    private readonly SyncOptions _options;
    private readonly IReadOnlyList<IScoreSource> _sources;
    private readonly IReadOnlyList<IScoreWriter> _writers;
    private readonly PinballApiClient _api;
    private readonly ILogger<ScoreSyncRunner> _log;

    public ScoreSyncRunner(
        SyncOptions options,
        IReadOnlyList<IScoreSource> sources,
        IReadOnlyList<IScoreWriter> writers,
        PinballApiClient api,
        ILogger<ScoreSyncRunner> log)
    {
        _options = options;
        _sources = sources;
        _writers = writers;
        _api = api;
        _log = log;
    }

    public async Task<SyncReport> RunAsync(CancellationToken cancellationToken = default)
    {
        var (scores, read, skipped) = Extract();

        _log.LogInformation("Read {Scores} scores from {Read} tables ({Skipped} skipped)",
            scores.Count, read, skipped);

        var submission = await SubmitAsync(scores, cancellationToken).ConfigureAwait(false);
        var written = await WriteBackAsync(scores, cancellationToken).ConfigureAwait(false);

        return new SyncReport(
            read,
            skipped,
            scores.Count,
            submission?.Inserted ?? 0,
            submission?.Duplicates ?? 0,
            submission?.Rejected ?? 0,
            written);
    }

    private (List<ScoreEntry> Scores, int Read, int Skipped) Extract()
    {
        var scores = new List<ScoreEntry>();
        int read = 0, skipped = 0;

        foreach (var source in _sources)
        {
            foreach (var result in source.Extract())
            {
                if (result.Skipped is not null)
                {
                    skipped++;
                    // Expected for unmapped ROMs and placeholder files, so not a warning.
                    _log.LogInformation("Skipped {Table} via {Source}: {Reason}",
                        result.Table, source.Name, result.Skipped);
                    continue;
                }

                read++;
                scores.AddRange(result.Scores.Where(IsSubmittable));
            }
        }

        return (scores, read, skipped);
    }

    /// <summary>
    /// Filters out placeholder scores we wrote ourselves to blank a machine, so
    /// blanking a board never feeds its own filler values back into the API.
    /// </summary>
    private bool IsSubmittable(ScoreEntry entry) =>
        !_options.PlaceholderInitials.Contains(entry.Player, StringComparer.OrdinalIgnoreCase);

    private async Task<SubmitResponse?> SubmitAsync(
        IReadOnlyList<ScoreEntry> scores,
        CancellationToken cancellationToken)
    {
        if (scores.Count == 0) return null;

        if (_options.DryRun)
        {
            _log.LogInformation("Dry run — would submit {Count} scores:", scores.Count);
            foreach (var entry in scores)
                _log.LogInformation("  would submit {Entry}", entry);
            return null;
        }

        try
        {
            var response = await _api.SubmitAsync(scores, cancellationToken).ConfigureAwait(false);
            if (response is null) return null;

            // Duplicates are the normal case — the same board is resubmitted every run.
            _log.LogInformation("Submitted {Received}: {Inserted} new, {Duplicates} duplicate, {Rejected} rejected",
                response.Received, response.Inserted, response.Duplicates, response.Rejected);

            foreach (var rejected in response.Results.Where(r => r.WasRejected))
                _log.LogWarning("Rejected {Table}/{Category} {Initials} {Value}: {Reason}",
                    rejected.Table, rejected.Category ?? "(main)", rejected.Initials, rejected.Value, rejected.Reason);

            foreach (var inserted in response.Results.Where(r => r.WasInserted))
                _log.LogInformation("New score {Table}/{Category} {Initials} {Value}",
                    inserted.Table, inserted.Category ?? "(main)", inserted.Initials, inserted.Value);

            return response;
        }
        catch (Exception ex) when (ex is PinballApiException or HttpRequestException or TaskCanceledException)
        {
            // The board is still on the machine and will be resubmitted next run.
            _log.LogError(ex, "Submission failed; scores remain on the machine and will retry");
            return null;
        }
    }

    private async Task<int> WriteBackAsync(IReadOnlyList<ScoreEntry> scores, CancellationToken cancellationToken)
    {
        // A dry run still computes and reports the plan — that is the point of it.
        if ((!_options.EnableWriteBack && !_options.DryRun) || _writers.Count == 0) return 0;

        if (!_options.DryRun && RunningGame() is { } game)
        {
            // The game rewrites its save data on exit and would discard our write.
            _log.LogInformation("Skipping write-back: {Process} is running", game);
            return 0;
        }

        var written = 0;
        foreach (var table in scores.Select(s => s.Table).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var writer = _writers.FirstOrDefault(w => w.Handles(table));
            if (writer is null) continue;

            try
            {
                var slots = Math.Max(writer.SlotCount(table, null), 1);
                var board = await _api.GetBoardAsync(table, slots, cancellationToken).ConfigureAwait(false);
                var result = await writer.WriteAsync(table, board, cancellationToken).ConfigureAwait(false);

                if (result.Applied) written++;
                else _log.LogDebug("Write-back for {Table} not applied: {Reason}", table, result.Skipped);

                // In a dry run the plan is the deliverable, so it is reported rather
                // than buried at debug level.
                foreach (var line in result.Planned)
                {
                    if (_options.DryRun) _log.LogInformation("  would write {Table} {Line}", table, line);
                    else _log.LogDebug("  {Table} {Line}", table, line);
                }
            }
            catch (Exception ex) when (ex is PinballApiException or HttpRequestException or TaskCanceledException)
            {
                _log.LogError(ex, "Write-back failed for {Table}", table);
            }
        }

        return written;
    }

    private string? RunningGame()
    {
        foreach (var name in _options.BlockingProcesses)
        {
            try
            {
                if (System.Diagnostics.Process.GetProcessesByName(name).Length > 0) return name;
            }
            catch (InvalidOperationException)
            {
                // Process list unavailable; treat as not running rather than failing the run.
            }
        }

        return null;
    }

    /// <summary>Builds the sources, writers and client described by the options.</summary>
    public static ScoreSyncRunner Create(SyncOptions options, ILoggerFactory loggerFactory, HttpClient? http = null)
    {
        var catalog = MapCatalog.Load(options.MapOverridePath);
        var sources = new List<IScoreSource>();
        var writers = new List<IScoreWriter>();

        // The first configured marker is what gets written when blanking a slot;
        // every marker in the list is ignored on the way back in.
        var placeholder = new Placeholder(
            options.PlaceholderInitials.FirstOrDefault() ?? Placeholder.Default.Initials,
            options.PlaceholderValue);

        if (!string.IsNullOrWhiteSpace(options.NvramPath))
        {
            sources.Add(new NvramScoreSource(options.NvramPath, catalog));
            writers.Add(new NvramScoreWriter(catalog, placeholder));
        }

        if (!string.IsNullOrWhiteSpace(options.VpRegPath))
        {
            var stg = new StgScoreSource(options.VpRegPath);
            sources.Add(stg);
            writers.Add(new StgScoreWriter(
                options.VpRegPath,
                stg.Extract().Select(r => r.Table).ToHashSet(StringComparer.OrdinalIgnoreCase),
                placeholder));
        }

        var api = new PinballApiClient(new PinballApiOptions
        {
            BaseUrl = options.ApiBaseUrl,
            ApiKey = options.ApiKey,
            Source = options.Source,
        }, http);

        return new ScoreSyncRunner(options, sources, writers, api, loggerFactory.CreateLogger<ScoreSyncRunner>());
    }
}
