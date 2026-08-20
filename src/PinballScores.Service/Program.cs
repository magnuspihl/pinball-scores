using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PinballScores.Core;
using PinballScores.Service;
using Velopack;

// Must run before anything else: this is where Velopack handles the install,
// update and uninstall hooks it invokes the executable for.
VelopackApp.Build().Run();

// Valueless switches must be removed before the configuration parser sees them:
// AddCommandLine treats "--once" as a key awaiting a value and would otherwise
// swallow the following argument, silently discarding a setting.
string[] switches = ["--once", "--plan"];
bool HasSwitch(string name) => args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

// --plan is a read-only rehearsal, so it implies a single run.
var dryRun = HasSwitch("--plan");
var runOnce = HasSwitch("--once") || dryRun;
var configArgs = args.Where(a => !switches.Contains(a, StringComparer.OrdinalIgnoreCase)).ToArray();

var builder = Host.CreateApplicationBuilder(configArgs);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile(
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "PinballScores",
            "appsettings.json"),
        optional: true,
        reloadOnChange: true)
    .AddEnvironmentVariables("PINBALLSCORES_")
    .AddCommandLine(configArgs);

if (dryRun)
{
    builder.Configuration.AddInMemoryCollection(
        new Dictionary<string, string?> { [$"{SyncOptions.SectionName}:DryRun"] = "true" });
}

builder.Services.Configure<SyncOptions>(builder.Configuration.GetSection(SyncOptions.SectionName));
builder.Services.Configure<AutoUpdateOptions>(builder.Configuration.GetSection(AutoUpdateOptions.SectionName));

// Never write to the console: there is no console, and anything that tried to
// create one would risk a window appearing on the cabinet.
builder.Logging.ClearProviders();
builder.Logging.AddProvider(new FileLoggerProvider(LogDirectory()));
if (OperatingSystem.IsWindows() && WindowsServiceHelpers.IsWindowsService())
{
    WindowsLogging.AddEventLog(builder.Logging);
}

builder.Services.AddSingleton<RunQueue>();
builder.Services.AddSingleton<UpdateChecker>();
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<SyncOptions>>().Value;
    var problems = options.Validate().ToList();
    if (problems.Count > 0)
        throw new OptionsValidationException(SyncOptions.SectionName, typeof(SyncOptions), problems);

    return ScoreSyncRunner.Create(options, sp.GetRequiredService<ILoggerFactory>());
});

if (runOnce)
{
    // Manual/diagnostic mode: one pass, then exit. Also the fallback if the
    // service model ever proves awkward and Task Scheduler is needed again.
    using var host = builder.Build();
    var logger = host.Services.GetRequiredService<ILogger<Program>>();
    var effective = host.Services.GetRequiredService<IOptions<SyncOptions>>().Value;
    // Log what was actually resolved: a mistyped path is otherwise invisible,
    // showing up only as a table silently producing no scores.
    logger.LogInformation("Config: nvram={Nvram} vpreg={VpReg} api={Api} writeBack={WriteBack}",
        effective.NvramPath, effective.VpRegPath, effective.ApiBaseUrl, effective.EnableWriteBack);
    try
    {
        var report = await host.Services.GetRequiredService<ScoreSyncRunner>().RunAsync();
        logger.LogInformation(
            "Single run finished: {Found} scores from {Tables} tables, {Inserted} new, {Duplicates} duplicate, {Rejected} rejected",
            report.ScoresFound, report.TablesRead, report.Inserted, report.Duplicates, report.Rejected);
        return report.Rejected == 0 ? 0 : 1;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Single run failed");
        return 2;
    }
}

builder.Services.AddHostedService<SyncWorker>();
builder.Services.AddWindowsService(options => options.ServiceName = "PinballScores");

await builder.Build().RunAsync();
return 0;

static string LogDirectory() => Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "PinballScores",
    "logs");

public partial class Program;
