using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

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

    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "PinballScores",
        "logs");

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
