using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Velopack;
using Velopack.Sources;

namespace PinballScores.Service;

public sealed class AutoUpdateOptions
{
    public const string SectionName = "Updates";

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// GitHub repository publishing the release packages. A public repository needs
    /// no credential on the cabinet at all, which is the main reason to prefer it.
    /// </summary>
    public string? RepositoryUrl { get; set; }

    /// <summary>Token for a private repository. Leave empty when the repo is public.</summary>
    public string? AccessToken { get; set; }

    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Accept pre-release builds. Useful for testing an update on the cabinet.</summary>
    public bool AllowPrerelease { get; set; }
}

/// <summary>
/// Checks for and stages a new release using Velopack, which handles packaging,
/// delta downloads, atomic swap and rollback.
///
/// The update is staged and applied on exit rather than mid-process: the service
/// stops itself once the new version is ready and the Windows Service Manager
/// restarts it (configure the service's recovery action to "Restart the Service").
/// Applying in-place while a run is active could swap binaries during a write to a
/// machine's save data.
/// </summary>
public sealed class UpdateChecker
{
    private readonly AutoUpdateOptions _options;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<UpdateChecker> _log;
    private readonly TimeProvider _time;
    private DateTimeOffset _nextCheck = DateTimeOffset.MinValue;
    private bool _pendingRestart;

    public UpdateChecker(
        IOptions<AutoUpdateOptions> options,
        IHostApplicationLifetime lifetime,
        ILogger<UpdateChecker> log,
        TimeProvider? time = null)
    {
        _options = options.Value;
        _lifetime = lifetime;
        _log = log;
        _time = time ?? TimeProvider.System;
    }

    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled || _pendingRestart) return;
        if (string.IsNullOrWhiteSpace(_options.RepositoryUrl)) return;
        if (_time.GetUtcNow() < _nextCheck) return;

        _nextCheck = _time.GetUtcNow() + _options.CheckInterval;

        try
        {
            var source = new GithubSource(_options.RepositoryUrl, _options.AccessToken, _options.AllowPrerelease);
            var manager = new UpdateManager(source);

            // False when running from a plain build rather than an installed package.
            if (!manager.IsInstalled)
            {
                _log.LogDebug("Not running from an installed package; skipping update check");
                return;
            }

            var update = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update is null)
            {
                _log.LogDebug("No update available");
                return;
            }

            _log.LogInformation("Downloading update {Version}", update.TargetFullRelease.Version);
            await manager.DownloadUpdatesAsync(update, cancelToken: cancellationToken).ConfigureAwait(false);

            // Staged only. It lands after this process exits, so no swap can occur
            // while a run is in flight.
            manager.WaitExitThenApplyUpdates(update);
            _pendingRestart = true;

            _log.LogInformation("Update {Version} staged; stopping for the service manager to restart us",
                update.TargetFullRelease.Version);
            _lifetime.StopApplication();
        }
        catch (Exception ex)
        {
            // An unreachable update server must never stop scores being collected.
            _log.LogWarning(ex, "Update check failed");
        }
    }
}
