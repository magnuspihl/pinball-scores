using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PinballScores.Core;

namespace PinballScores.Service;

/// <summary>
/// The long-running worker. Two things ask for a run — the schedule and a file
/// change — and both go through one queue so runs never overlap.
///
/// The scheduled tick matters even when nothing changed: write-back has to push the
/// API's board onto machines regardless of local activity.
/// </summary>
public sealed class SyncWorker : BackgroundService
{
    private readonly SyncOptions _options;
    private readonly ScoreSyncRunner _runner;
    private readonly RunQueue _queue;
    private readonly UpdateChecker _updates;
    private readonly ILogger<SyncWorker> _log;

    public SyncWorker(
        IOptions<SyncOptions> options,
        ScoreSyncRunner runner,
        RunQueue queue,
        UpdateChecker updates,
        ILogger<SyncWorker> log)
    {
        _options = options.Value;
        _runner = runner;
        _queue = queue;
        _updates = updates;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var watcher = new FileChangeWatcher(_queue, _options.DebounceDelay, _log);
        if (!string.IsNullOrWhiteSpace(_options.NvramPath))
            watcher.WatchDirectory(_options.NvramPath, "*.nv");
        if (!string.IsNullOrWhiteSpace(_options.VpRegPath))
            watcher.WatchFile(_options.VpRegPath);

        _queue.Request(RunTrigger.Startup);
        var ticker = RunScheduleAsync(stoppingToken);

        try
        {
            await foreach (var trigger in _queue.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                await RunOnceAsync(trigger, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        finally
        {
            _queue.Complete();
            await ticker.ConfigureAwait(false);
        }
    }

    private async Task RunScheduleAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                _queue.Request(RunTrigger.Scheduled);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private async Task RunOnceAsync(RunTrigger trigger, CancellationToken stoppingToken)
    {
        try
        {
            _log.LogInformation("Run starting ({Trigger})", trigger);
            var report = await _runner.RunAsync(stoppingToken).ConfigureAwait(false);
            _log.LogInformation(
                "Run finished: {Found} scores, {Inserted} new, {Duplicates} duplicate, {Rejected} rejected",
                report.ScoresFound, report.Inserted, report.Duplicates, report.Rejected);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A failed run must never take the service down; the next tick retries.
            _log.LogError(ex, "Run failed");
        }

        // Updating between runs, never during one, so a swap cannot land mid-write.
        await _updates.CheckAsync(stoppingToken).ConfigureAwait(false);
    }
}
