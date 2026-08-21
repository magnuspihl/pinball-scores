namespace PinballScores.Core;

/// <summary>
/// Everything the CLI needs to know about this cabinet. Bound from appsettings.json,
/// environment variables or command line — never compiled in, unlike the old build
/// which had a Firebase private key and Slack webhooks hardcoded in source.
/// </summary>
public sealed class SyncOptions
{
    public const string SectionName = "PinballScores";

    /// <summary>Folder holding VPinMAME .nv files.</summary>
    public string? NvramPath { get; set; }

    /// <summary>Path to Visual Pinball's shared VPReg.stg.</summary>
    public string? VpRegPath { get; set; }

    /// <summary>Optional folder of extra or updated memory maps, loaded over the bundled ones.</summary>
    public string? MapOverridePath { get; set; }

    public string ApiBaseUrl { get; set; } = "";

    public string? ApiKey { get; set; }

    /// <summary>Identifies this cabinet in submitted rows.</summary>
    public string Source { get; set; } = "pinballscores-cli";

    /// <summary>How often a scheduled run happens. Insertion needs this even when no file changed.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Quiet period after a file change before reading it. Games write their save
    /// data in bursts, so reacting to the first event risks reading a half-written file.
    /// </summary>
    public TimeSpan DebounceDelay { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>Write the API's board back into the machines. Stubbed for now.</summary>
    public bool EnableWriteBack { get; set; }

    /// <summary>
    /// Read-only rehearsal: report what would be submitted and what would be written
    /// to each machine, without POSTing anything or touching a save file. Intended
    /// for verifying a cabinet's state before and after clearing it.
    /// </summary>
    public bool DryRun { get; set; }

    /// <summary>
    /// Emulators that must not be running before anything is written to a machine.
    /// A game holds its save data in memory and flushes it on exit, so writing while
    /// one is open is simply overwritten.
    ///
    /// Matched by prefix, so "VPinballX" also catches "VPinballX64" and
    /// "VPinballX_GL". List the emulators only — a front end such as PinUp Popper
    /// runs the whole time the cabinet is on, and listing it would disable
    /// write-back permanently.
    /// </summary>
    public IList<string> BlockingProcesses { get; set; } =
        ["VPinballX", "VPinball995", "PinballFX", "Future Pinball", "PinballArcade"];

    /// <summary>
    /// Initials used for placeholder scores written to blank a machine. Any score
    /// held by these is ignored on extraction, so blanking a board to "beat me
    /// immediately" values never repopulates the API with its own filler.
    ///
    /// The marker itself is blank, which is already treated as an unused slot, so
    /// this list only needs entries for historical markers. "---" was the marker
    /// until Williams WPC was found to reject dashes.
    /// </summary>
    public IList<string> PlaceholderInitials { get; set; } = ["---"];

    /// <summary>Marker initials written when blanking a slot. A space; see <see cref="Insertion.Placeholder"/>.</summary>
    public string PlaceholderMarker { get; set; } = " ";

    /// <summary>
    /// Value written alongside <see cref="PlaceholderMarker"/>. Low enough that any
    /// real play beats it immediately, but non-zero: a cleared record reads as
    /// invalid and the ROM restores its factory default in place of it.
    /// </summary>
    public long PlaceholderValue { get; set; } = 1;

    /// <summary>
    /// Tables to leave alone entirely. Visual Pinball's VPReg.stg is shared, so it
    /// can hold storages for tables that are not part of the cabinet's tracked set
    /// and are neither reset nor wanted on the leaderboard.
    /// </summary>
    public IList<string> IgnoredTables { get; set; } = [];

    public IEnumerable<string> Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiBaseUrl))
            yield return $"{SectionName}:ApiBaseUrl is required";
        else if (!Uri.TryCreate(ApiBaseUrl, UriKind.Absolute, out _))
            yield return $"{SectionName}:ApiBaseUrl is not a valid absolute URL";

        if (string.IsNullOrWhiteSpace(NvramPath) && string.IsNullOrWhiteSpace(VpRegPath))
            yield return $"{SectionName}: at least one of NvramPath or VpRegPath must be set";

        if (Interval < TimeSpan.FromMinutes(1))
            yield return $"{SectionName}:Interval must be at least one minute";
    }
}
