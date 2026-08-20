using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace PinballScores.Service;

/// <summary>
/// Windows Event Log wiring, isolated so the platform guard applies to the
/// configuration lambda as well as the call site.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsLogging
{
    /// <summary>
    /// Service start/stop failures surface in Event Viewer, which is where an
    /// operator looks first when the service will not come up.
    /// </summary>
    public static void AddEventLog(ILoggingBuilder logging) =>
        EventLoggerFactoryExtensions.AddEventLog(logging, settings => settings.SourceName = "PinballScores");
}
