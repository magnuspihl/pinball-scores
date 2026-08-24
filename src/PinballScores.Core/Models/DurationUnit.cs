namespace PinballScores.Core.Models;

/// <summary>
/// The units a <c>value_type: "duration"</c> score can be submitted in. The API's
/// canonical storage is always milliseconds; these are the raw field units the CLI
/// reads off a machine and writes back, kept alongside the value so the server can
/// convert with integer math and the CLI never has to round.
/// </summary>
public static class DurationUnit
{
    /// <summary>How many milliseconds one unit of <paramref name="unit"/> is.</summary>
    public static long MillisecondsPerUnit(string unit) => unit.ToLowerInvariant() switch
    {
        "ms" => 1,
        "cs" => 10,
        "ds" => 100,
        "s" => 1_000,
        "m" => 60_000,
        "h" => 3_600_000,
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "unknown duration unit"),
    };
}
