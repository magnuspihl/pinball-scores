using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
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

    /// <summary>
    /// How often to look for a new release. The check is two small HTTPS requests,
    /// so this can be short; an hour is plenty.
    ///
    /// Careful writing this in JSON: .NET reads a leading component above 23 as
    /// *days*, so "24:00:00" is twenty-four days, not one. Use "1.00:00:00" for a
    /// day, or just "01:00:00" for an hour.
    /// </summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Floor applied to <see cref="CheckInterval"/>. Runs can be triggered by a file
    /// change every few seconds, and unauthenticated GitHub API access is limited to
    /// 60 requests an hour per IP, so a mistyped interval must not turn into a poll.
    /// </summary>
    public static readonly TimeSpan MinimumCheckInterval = TimeSpan.FromMinutes(1);

    /// <summary>The interval actually used, with the floor applied.</summary>
    public TimeSpan EffectiveCheckInterval =>
        CheckInterval < MinimumCheckInterval ? MinimumCheckInterval : CheckInterval;

    /// <summary>Accept pre-release builds. Useful for testing an update on the cabinet.</summary>
    public bool AllowPrerelease { get; set; }

    /// <summary>Service name used to start ourselves again after an update.</summary>
    public string ServiceName { get; set; } = "PinballScores";

    /// <summary>
    /// How long the restart helper waits before starting the service again, giving
    /// this process time to exit and Velopack time to swap the files.
    /// </summary>
    public TimeSpan RestartDelay { get; set; } = TimeSpan.FromSeconds(20);
}

/// <summary>
/// Checks for and stages a new release using Velopack, which handles packaging,
/// delta downloads, atomic swap and rollback.
///
/// The update is staged and applied on exit rather than mid-process: applying it
/// in place while a run is active could swap binaries during a write to a machine's
/// save data. So the service stages the package, schedules its own restart, and
/// stops; Velopack swaps the files while nothing is running.
///
/// The restart is done by a detached helper rather than by Windows Service Manager
/// recovery actions. Recovery actions are not usable here: they only fire when a
/// service terminates *without* reporting SERVICE_STOPPED, or — with the failure
/// actions flag set — when it stops with a non-zero exit code. A clean stop like
/// ours reports SERVICE_STOPPED with exit code 0, so SCM would treat it as a normal
/// shutdown and never bring the service back. The cabinet would quietly stop
/// collecting scores after its first update.
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

        _nextCheck = _time.GetUtcNow() + _options.EffectiveCheckInterval;

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

            ScheduleRestart();

            _log.LogInformation("Update {Version} staged; stopping so it can be applied",
                update.TargetFullRelease.Version);
            _lifetime.StopApplication();
        }
        catch (Exception ex)
        {
            // An unreachable update server must never stop scores being collected.
            _log.LogWarning(ex, "Update check failed");
        }
    }

    /// <summary>
    /// Spawns a detached helper that waits for this process to exit and then starts
    /// the service again. Deterministic, and independent of how the service's
    /// recovery actions happen to be configured.
    ///
    /// Only meaningful when actually running as a service — an interactive run has
    /// nothing to restart, and starting the service from a developer machine would
    /// be surprising.
    /// </summary>
    private void ScheduleRestart()
    {
        if (!OperatingSystem.IsWindows() || !WindowsServiceHelpers.IsWindowsService())
        {
            _log.LogInformation("Not running as a service; the update applies on next start");
            return;
        }

        var seconds = Math.Max((int)_options.RestartDelay.TotalSeconds, 1);
        var service = _options.ServiceName;

        try
        {
            // CreateNoWindow and UseShellExecute=false matter here: nothing may flash
            // up on the cabinet's screen or take focus.
            using var helper = Process.Start(new ProcessStartInfo("cmd.exe")
            {
                Arguments = $"/c timeout /t {seconds} /nobreak >nul & sc start \"{service}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            _log.LogInformation("Restart of {Service} scheduled in {Seconds}s", service, seconds);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Better to stay running on the old version than to stop and not come back.
            _log.LogError(ex, "Could not schedule a restart; skipping this update to stay running");
            _pendingRestart = false;
            throw;
        }
    }
}
