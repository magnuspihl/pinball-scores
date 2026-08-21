using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PinballScores.Core;

namespace PinballScores.Service;

/// <summary>
/// Builds the host with its configuration sources.
///
/// Extracted from Program so the configuration layering can be tested — in
/// particular that it does not depend on the working directory.
/// </summary>
public static class ServiceHost
{
    /// <summary>Machine-wide settings, which override the copy shipped in the package.</summary>
    public static string MachineSettingsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "PinballScores",
        "appsettings.json");

    /// <summary>Defaults shipped inside the package. Replaced by every update.</summary>
    public static string PackagedSettingsPath => Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    /// <summary>
    /// Reports which settings files were actually found and what they resolved to.
    ///
    /// Two files with the same name and very different lifetimes is inherently
    /// confusing — the packaged one is replaced by every update, the machine one
    /// never is — so the log has to say which was in play. Without it, editing the
    /// wrong file looks identical to the settings not applying.
    /// </summary>
    public static void LogEffectiveSettings(IServiceProvider services)
    {
        var log = services.GetRequiredService<ILogger<Program>>();
        var options = services.GetRequiredService<IOptions<SyncOptions>>().Value;

        log.LogInformation("Settings: packaged={Packaged} machine={Machine}",
            File.Exists(PackagedSettingsPath) ? PackagedSettingsPath : "(none)",
            File.Exists(MachineSettingsPath) ? MachineSettingsPath : "(none - using packaged defaults)");

        log.LogInformation("Config: nvram={Nvram} vpreg={VpReg} api={Api} writeBack={WriteBack} ignored={Ignored}",
            options.NvramPath, options.VpRegPath, options.ApiBaseUrl, options.EnableWriteBack,
            options.IgnoredTables.Count == 0 ? "(none)" : string.Join(",", options.IgnoredTables));
    }

    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "PinballScores",
        "logs");

    /// <summary>
    /// Records a failure that happened before, or instead of, normal logging.
    ///
    /// The app is a WinExe with no console and runs as a service, so an exception
    /// during startup would otherwise be completely invisible: Windows reports only
    /// "cannot start service on computer '.'", and configuration is loaded before
    /// the file logger exists. A malformed appsettings.json is the likely cause —
    /// "optional" covers a missing file, not an invalid one, so a stray comma there
    /// takes the service down silently.
    /// </summary>
    public static void WriteStartupError(Exception ex)
    {
        foreach (var directory in new[] { LogDirectory, Path.Combine(Path.GetTempPath(), "PinballScores") })
        {
            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(
                    Path.Combine(directory, "startup-error.log"),
                    $"""
                     {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss'Z'} PinballScores failed to start.

                     Packaged settings: {Path.Combine(AppContext.BaseDirectory, "appsettings.json")}
                     Machine settings:  {MachineSettingsPath}

                     {ex}
                     """);
                return;
            }
            catch (Exception writeFailure) when (writeFailure is IOException or UnauthorizedAccessException)
            {
                // Try the next location.
            }
        }
    }

    public static HostApplicationBuilder CreateBuilder(string[] configArgs)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = configArgs,

            // A Windows service starts with its working directory set to
            // C:\Windows\System32, and the content root otherwise defaults to the
            // current directory. Unlike IHostBuilder.UseWindowsService(), the
            // IServiceCollection AddWindowsService() cannot correct that, so the
            // packaged appsettings.json would never be found: configuration would
            // come back empty, validation would fail, and the service would die
            // before starting with only a generic "cannot start service" from SCM.
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.Configuration
            // Explicitly rooted rather than relative, so this holds no matter how
            // the process was launched.
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
                optional: true, reloadOnChange: true)
            .AddJsonFile(MachineSettingsPath, optional: true, reloadOnChange: true)
            .AddEnvironmentVariables("PINBALLSCORES_")
            .AddCommandLine(configArgs);

        return builder;
    }
}
