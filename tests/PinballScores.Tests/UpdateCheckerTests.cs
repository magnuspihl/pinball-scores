using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PinballScores.Service;
using Xunit;

namespace PinballScores.Tests;

internal sealed class StubLifetime : IHostApplicationLifetime
{
    public int StopCount { get; private set; }
    public CancellationToken ApplicationStarted => CancellationToken.None;
    public CancellationToken ApplicationStopping => CancellationToken.None;
    public CancellationToken ApplicationStopped => CancellationToken.None;
    public void StopApplication() => StopCount++;
}

internal sealed class StubClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>
/// Covers the guards around the update check. The Velopack download and swap
/// itself cannot run here — it needs an installed package on Windows — but these
/// are the paths that decide whether it is even attempted.
/// </summary>
public class UpdateCheckerTests
{
    private static (UpdateChecker Checker, StubLifetime Lifetime, StubClock Clock) Build(
        Action<AutoUpdateOptions>? configure = null)
    {
        var options = new AutoUpdateOptions { RepositoryUrl = "https://github.com/example/repo" };
        configure?.Invoke(options);

        var lifetime = new StubLifetime();
        var clock = new StubClock(DateTimeOffset.UnixEpoch);
        var checker = new UpdateChecker(
            new OptionsWrapper<AutoUpdateOptions>(options),
            lifetime,
            NullLogger<UpdateChecker>.Instance,
            clock);

        return (checker, lifetime, clock);
    }

    [Fact]
    public async Task DisabledMeansNoCheckAndNoStop()
    {
        var (checker, lifetime, _) = Build(o => o.Enabled = false);

        await checker.CheckAsync(CancellationToken.None);

        Assert.Equal(0, lifetime.StopCount);
    }

    [Fact]
    public async Task WithoutARepositoryNothingHappens()
    {
        var (checker, lifetime, _) = Build(o => o.RepositoryUrl = null);

        await checker.CheckAsync(CancellationToken.None);

        Assert.Equal(0, lifetime.StopCount);
    }

    [Fact]
    public async Task AnUnreachableUpdateServerNeverStopsTheService()
    {
        // Collecting scores matters more than being on the latest version.
        var (checker, lifetime, _) = Build(o => o.RepositoryUrl = "https://invalid.invalid/nope");

        await checker.CheckAsync(CancellationToken.None);

        Assert.Equal(0, lifetime.StopCount);
    }

    [Fact]
    public async Task ChecksAreRateLimitedToTheConfiguredInterval()
    {
        // Runs can be triggered by a file change every few seconds; the update check
        // must not follow them.
        var (checker, _, clock) = Build(o =>
        {
            o.RepositoryUrl = "https://invalid.invalid/nope";
            o.CheckInterval = TimeSpan.FromHours(24);
        });

        var first = clock.Now;
        await checker.CheckAsync(CancellationToken.None);

        // Well inside the interval: must be a no-op, and fast.
        clock.Now = first.AddHours(1);
        var started = DateTimeOffset.UtcNow;
        await checker.CheckAsync(CancellationToken.None);
        Assert.True(DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void RestartIsNotLeftToServiceRecoveryActions()
    {
        // Regression guard for a real defect: StopApplication is a clean stop that
        // reports SERVICE_STOPPED with exit code 0, which Windows treats as a normal
        // shutdown. Recovery actions would never fire and the cabinet would stop
        // collecting scores after its first update, so the updater must schedule its
        // own restart and needs a service name to do it with.
        var options = new AutoUpdateOptions();

        Assert.False(string.IsNullOrWhiteSpace(options.ServiceName));
        Assert.True(options.RestartDelay > TimeSpan.Zero);
    }

    [Fact]
    public void UpdatesAreOnByDefaultButInertWithoutARepository()
    {
        var options = new AutoUpdateOptions();

        Assert.True(options.Enabled);
        Assert.Null(options.RepositoryUrl);
        Assert.Equal(TimeSpan.FromHours(24), options.CheckInterval);
    }
}
