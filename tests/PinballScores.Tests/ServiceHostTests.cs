using Microsoft.Extensions.Configuration;
using PinballScores.Core;
using PinballScores.Service;
using Xunit;

namespace PinballScores.Tests;

/// <summary>
/// A Windows service starts with its working directory set to C:\Windows\System32.
/// Configuration must not depend on that, or the packaged appsettings.json is never
/// read, validation fails, and the service dies at startup with nothing but SCM's
/// generic "cannot start service" to go on.
/// </summary>
[Collection(nameof(WorkingDirectoryTests))]
public class ServiceHostTests
{
    [Fact]
    public void PackagedSettingsAreFoundFromAnyWorkingDirectory()
    {
        var original = Directory.GetCurrentDirectory();
        var elsewhere = Directory.CreateTempSubdirectory("svc-cwd-");
        try
        {
            Directory.SetCurrentDirectory(elsewhere.FullName);

            var builder = ServiceHost.CreateBuilder([]);
            var options = new SyncOptions();
            builder.Configuration.GetSection(SyncOptions.SectionName).Bind(options);

            // The packaged appsettings.json ships an API base URL; reading it proves
            // the file was located relative to the binary rather than the CWD.
            Assert.False(string.IsNullOrWhiteSpace(options.ApiBaseUrl),
                "appsettings.json was not found from a foreign working directory");
            Assert.Empty(options.Validate());
        }
        finally
        {
            Directory.SetCurrentDirectory(original);
            elsewhere.Delete(recursive: true);
        }
    }

    [Fact]
    public void ContentRootIsTheBinaryDirectoryNotTheWorkingDirectory()
    {
        var original = Directory.GetCurrentDirectory();
        var elsewhere = Directory.CreateTempSubdirectory("svc-root-");
        try
        {
            Directory.SetCurrentDirectory(elsewhere.FullName);
            var builder = ServiceHost.CreateBuilder([]);

            Assert.Equal(
                Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory),
                Path.TrimEndingDirectorySeparator(builder.Environment.ContentRootPath));
        }
        finally
        {
            Directory.SetCurrentDirectory(original);
            elsewhere.Delete(recursive: true);
        }
    }

    [Fact]
    public void MachineSettingsPathIsAbsoluteSoItIsAlsoCwdIndependent()
    {
        Assert.True(Path.IsPathRooted(ServiceHost.MachineSettingsPath));
        Assert.True(Path.IsPathRooted(ServiceHost.LogDirectory));
    }

    [Fact]
    public void AStartupFailureIsWrittenSomewhereFindable()
    {
        // Without this a bad config is completely silent: no console (WinExe), and
        // configuration loads before the file logger exists, so Windows reports only
        // "cannot start service on computer '.'".
        ServiceHost.WriteStartupError(new InvalidDataException("boom: bad appsettings"));

        var candidates = new[]
        {
            Path.Combine(ServiceHost.LogDirectory, "startup-error.log"),
            Path.Combine(Path.GetTempPath(), "PinballScores", "startup-error.log"),
        };

        var written = candidates.FirstOrDefault(File.Exists);
        Assert.NotNull(written);

        var text = File.ReadAllText(written);
        Assert.Contains("boom: bad appsettings", text);
        // Must say which files were in play, since that is the usual cause.
        Assert.Contains(ServiceHost.MachineSettingsPath, text);
    }

    [Fact]
    public void AMalformedOptionalSettingsFileIsNotSilentlyIgnored()
    {
        // "optional" means "may be missing", not "may be invalid" — a stray comma in
        // a hand-edited config throws, which is why the crash log above exists.
        var path = Path.Combine(Directory.CreateTempSubdirectory("badcfg-").FullName, "appsettings.json");
        File.WriteAllText(path, "{ \"PinballScores\": { \"Source\": \"x\",, } }");

        Assert.Throws<InvalidDataException>(() =>
            new ConfigurationBuilder()
                .AddJsonFile(path, optional: true, reloadOnChange: false)
                .Build());
    }

    [Fact]
    public void AttachingToATerminalNeverCreatesOne()
    {
        // The whole reason the app is a WinExe is that a service must not be able to
        // put a window on the cabinet's screen. Attaching to a *parent* console is
        // safe because it cannot create one; when there is no parent — the service
        // case — it must simply report failure and stay silent.
        //
        // It must report failure rather than throwing, because the caller decides
        // whether to add a console logger based on the return value — an exception
        // here would take the service down at startup.
        var failure = Record.Exception(() => ConsoleOutput.TryAttachToParentTerminal());

        Assert.Null(failure);
    }

    [Fact]
    public void PackagedSettingsMayCarryCommentsForWarnings()
    {
        // The packaged file warns that it is replaced on update; that warning is a
        // // comment, so the reader has to tolerate them.
        var path = Path.Combine(Directory.CreateTempSubdirectory("cmtcfg-").FullName, "appsettings.json");
        File.WriteAllText(path, "{\n  // do not edit\n  \"PinballScores\": { \"Source\": \"ok\" }\n}");

        var cfg = new ConfigurationBuilder().AddJsonFile(path).Build();

        Assert.Equal("ok", cfg["PinballScores:Source"]);
    }

    [Fact]
    public void CommandLineStillOverridesTheFile()
    {
        var builder = ServiceHost.CreateBuilder(["--PinballScores:Source=override-test"]);
        var options = new SyncOptions();
        builder.Configuration.GetSection(SyncOptions.SectionName).Bind(options);

        Assert.Equal("override-test", options.Source);
    }
}

/// <summary>
/// These tests mutate process-wide current directory, so they must not run
/// alongside anything that reads relative paths.
/// </summary>
[CollectionDefinition(nameof(WorkingDirectoryTests), DisableParallelization = true)]
public class WorkingDirectoryTests;
