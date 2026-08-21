using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PinballScores.Core;
using PinballScores.Service;
using Velopack;

try
{
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

    var builder = ServiceHost.CreateBuilder(configArgs);

    if (dryRun)
    {
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { [$"{SyncOptions.SectionName}:DryRun"] = "true" });
    }

    builder.Services.Configure<SyncOptions>(builder.Configuration.GetSection(SyncOptions.SectionName));
    builder.Services.Configure<AutoUpdateOptions>(builder.Configuration.GetSection(AutoUpdateOptions.SectionName));

    // The service must never write to a console, because creating one would risk a
    // window appearing on the cabinet. The file log is always the record of record.
    builder.Logging.ClearProviders();
    builder.Logging.AddProvider(new FileLoggerProvider(ServiceHost.LogDirectory));

    if (OperatingSystem.IsWindows() && WindowsServiceHelpers.IsWindowsService())
    {
        WindowsLogging.AddEventLog(builder.Logging);
    }
    else if (runOnce && ConsoleOutput.TryAttachToParentTerminal())
    {
        // A person running --plan or --once by hand should see the result rather
        // than being told to go and read a log file. Attaching to the terminal that
        // launched us creates no window, and silently does nothing when there is no
        // terminal — which is precisely the service case.
        builder.Logging.AddSimpleConsole(o => o.SingleLine = true);
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
}
catch (Exception ex)
{
    // Nothing else will report this: no console, and configuration is loaded
    // before the file logger exists.
    ServiceHost.WriteStartupError(ex);
    return 3;
}

public partial class Program;
