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
    int TablesWritten,
    int Echoes = 0);

/// <summary>
/// One complete pass: read every machine, submit what was found, then write the
/// API's authoritative board back. Submission happens first so a score set since
/// the last run is banked before anything overwrites the machine.
///
/// That order is only safe because the submission reports which tables were read,
/// not just which scores were found. The server keeps the board it last saw on this
/// cabinet, so it can tell a score that has simply not been overwritten yet from one
/// that was genuinely achieved — a distinction this end cannot make, because native
/// save files carry no timestamps. Without it, lowering a board on the API (starting
/// a competition, deleting a row, wiping the database) would be undone by the very
/// next run: the machine still holds the old scores, posts them before write-back
/// gets a chance to correct it, and they return as newly achieved.
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
        var (scores, tables, skipped) = Extract();

        _log.LogInformation("Read {Scores} scores from {Read} tables ({Skipped} skipped)",
            scores.Count, tables.Count, skipped);

        var submission = await SubmitAsync(tables, scores, cancellationToken).ConfigureAwait(false);

        // Every table that was read, not just those that had scores. A blanked
        // machine yields nothing, and it is exactly the one that most needs the
        // API's board written onto it.
        var written = await WriteBackAsync(tables, cancellationToken).ConfigureAwait(false);

        return new SyncReport(
            tables.Count,
            skipped,
            scores.Count,
            submission?.Inserted ?? 0,
            submission?.Duplicates ?? 0,
            submission?.Rejected ?? 0,
            written,
            submission?.Echoes ?? 0);
    }

    private (List<ScoreEntry> Scores, List<string> Tables, int Skipped) Extract()
    {
        var scores = new List<ScoreEntry>();
        var tables = new List<string>();
        var skipped = 0;

        foreach (var source in _sources)
        {
            foreach (var result in source.Extract())
            {
                if (_options.IgnoredTables.Contains(result.Table, StringComparer.OrdinalIgnoreCase))
                {
                    skipped++;
                    _log.LogInformation("Ignored {Table} via {Source}: excluded by configuration",
                        result.Table, source.Name);
                    continue;
                }

                if (result.Skipped is not null)
                {
                    skipped++;

                    if (result.SkipIsRoutine)
                    {
                        // A VPinMAME nvram folder can hold dozens of ROMs this cabinet
                        // does not use. Reporting each one every run would bury the
                        // skips that actually mean something; the count still appears
                        // in the run summary.
                        _log.LogDebug("Skipped {Table} via {Source}: {Reason}",
                            result.Table, source.Name, result.Skipped);
                    }
                    else
                    {
                        // A table we do expect to read is not being read — its scores
                        // are being lost until someone looks.
                        _log.LogWarning("Skipped {Table} via {Source}: {Reason}",
                            result.Table, source.Name, result.Skipped);
                    }

                    continue;
                }

                tables.Add(result.Table);
                scores.AddRange(result.Scores.Where(IsSubmittable));
            }
        }

        // Distinct because this list is a claim about which boards were observed, and
        // the server counts one report per table. Write-back deduplicates too.
        return (scores, [.. tables.Distinct(StringComparer.OrdinalIgnoreCase)], skipped);
    }

    /// <summary>
    /// Filters out placeholder scores we wrote ourselves to blank a machine, so
    /// blanking a board never feeds its own filler values back into the API.
    /// </summary>
    private bool IsSubmittable(ScoreEntry entry) =>
        !_options.PlaceholderInitials.Contains(entry.Player, StringComparer.OrdinalIgnoreCase);

    private async Task<SubmitResponse?> SubmitAsync(
        IReadOnlyList<string> tables,
        IReadOnlyList<ScoreEntry> scores,
        CancellationToken cancellationToken)
    {
        // Driven by the tables, not the scores. A cabinet that was read and found
        // blank still has to say so — that report is what tells the server a clear
        // landed, and staying silent would look like a run that never happened.
        if (tables.Count == 0) return null;

        if (_options.DryRun)
        {
            _log.LogInformation("Dry run — would report {Tables} tables and submit {Count} scores:",
                tables.Count, scores.Count);
            foreach (var entry in scores)
                _log.LogInformation("  would submit {Entry}", entry);
            return null;
        }

        try
        {
            var response = await _api.SubmitAsync(tables, scores, cancellationToken).ConfigureAwait(false);
            if (response is null) return null;

            // Duplicates are the normal case — the same board is resubmitted every run.
            // Echoes are the same board resubmitted while the API holds something lower,
            // which is the window a clear has to survive.
            _log.LogInformation(
                "Submitted {Received} from {Tables} tables: {Inserted} new, {Duplicates} duplicate, {Echoes} echo, {Rejected} rejected",
                response.Received, tables.Count, response.Inserted, response.Duplicates, response.Echoes, response.Rejected);

            if (response.Echoes > 0)
                _log.LogInformation(
                    "{Echoes} scores held back as unchanged since the last report — the machine is behind the API and write-back should correct it",
                    response.Echoes);

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

    private async Task<int> WriteBackAsync(IReadOnlyList<string> tables, CancellationToken cancellationToken)
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
        foreach (var table in tables.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var writer = _writers.FirstOrDefault(w => w.Handles(table));
            if (writer is null) continue;

            try
            {
                var slots = Math.Max(writer.SlotCount(table, null), 1);
                var board = await _api.GetBoardAsync(table, slots, cancellationToken).ConfigureAwait(false);
                var result = await writer.WriteAsync(table, board, _options.DryRun, cancellationToken).ConfigureAwait(false);

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

    /// <summary>
    /// The first configured process that is running, or null.
    ///
    /// Matched by prefix rather than exactly, because the executable name varies by
    /// build — "VPinballX" has to catch "VPinballX64" and "VPinballX_GL" too. An
    /// exact match that silently fails is the dangerous case: write-back would then
    /// run while a table is open, and the emulator would overwrite it on exit.
    ///
    /// Note this should list the *emulators*, not the front end. A launcher such as
    /// PinUp Popper stays running the whole time the cabinet is on, so including it
    /// would disable write-back permanently.
    /// </summary>
    private string? RunningGame()
    {
        foreach (var name in _options.BlockingProcesses)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;

            try
            {
                var match = System.Diagnostics.Process.GetProcesses()
                    .FirstOrDefault(p => p.ProcessName.StartsWith(name, StringComparison.OrdinalIgnoreCase));
                if (match is not null) return match.ProcessName;
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

        var placeholder = new Placeholder(options.PlaceholderMarker, options.PlaceholderValue);

        if (!string.IsNullOrWhiteSpace(options.NvramPath))
        {
            sources.Add(new NvramScoreSource(options.NvramPath, catalog));
            writers.Add(new NvramScoreWriter(catalog, options.NvramPath, placeholder));
        }

        if (!string.IsNullOrWhiteSpace(options.VpRegPath))
        {
            sources.Add(new StgScoreSource(options.VpRegPath, catalog));
            writers.Add(new StgScoreWriter(options.VpRegPath, catalog, placeholder));
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
