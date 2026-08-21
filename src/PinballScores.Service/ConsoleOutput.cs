using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PinballScores.Service;

/// <summary>
/// Lets the one-shot modes print to the terminal they were launched from.
///
/// The app is built as <c>WinExe</c> so that running as a service can never
/// allocate a console window on the cabinet. The side effect is that
/// <c>--plan</c> and <c>--once</c> also print nothing when a person runs them by
/// hand, which makes them useless as diagnostics.
///
/// Attaching to the *parent* console fixes that without weakening the guarantee:
/// it never creates a window, and when there is no parent console — which is
/// exactly the case for a service — it simply fails and output stays silent.
/// </summary>
public static class ConsoleOutput
{
    private const uint AttachParentProcess = 0xFFFFFFFF;

    // DllImport rather than LibraryImport: the source-generated form requires
    // AllowUnsafeBlocks, which is not worth turning on project-wide for one call.
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint dwProcessId);

    /// <summary>
    /// Connects stdout to the launching terminal. Returns false when there is
    /// nothing to attach to, in which case nothing should be written.
    /// </summary>
    public static bool TryAttachToParentTerminal()
    {
        // Consoles work normally everywhere else.
        return !OperatingSystem.IsWindows() || AttachAndRebindStreams();
    }

    [SupportedOSPlatform("windows")]
    private static bool AttachAndRebindStreams()
    {
        if (!AttachConsole(AttachParentProcess)) return false;

        try
        {
            // The standard streams were bound before the console existed, so they
            // have to be reopened or writes go nowhere.
            var output = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            Console.SetOut(output);
            var error = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
            Console.SetError(error);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
