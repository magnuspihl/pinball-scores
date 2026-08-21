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
