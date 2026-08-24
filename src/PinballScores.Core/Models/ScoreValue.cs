using System.Globalization;

namespace PinballScores.Core.Models;

/// <summary>
/// How a value crosses the wire in both directions. Shared by the API models and
/// the NVRAM codecs so the two cannot drift: whatever is submitted must decode
/// back to the same bytes on write-back.
///
/// A timestamp is the reason this exists. A WPC real-time clock stores plain wall
/// clock — no zone, no offset — so it is submitted as text and converted with no
/// timezone applied in either direction. Treating it as an instant and converting
/// would move every date by the cabinet's offset, twice.
/// </summary>
public static class ScoreValue
{
    /// <summary>Wall-clock text as submitted, e.g. "2022-12-31 21:32".</summary>
    public const string ClockFormat = "yyyy-MM-dd HH:mm";

    public static string ClockText(long value) =>
        DateTimeOffset.FromUnixTimeSeconds(value).UtcDateTime.ToString(ClockFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// An integer, or wall-clock text, as the number the machine stores. Anything
    /// else is 0 — the callers treat that as "nothing to write".
    /// </summary>
    public static long Parse(string? text)
    {
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            return number;

        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var moment)
            ? new DateTimeOffset(DateTime.SpecifyKind(moment, DateTimeKind.Utc)).ToUnixTimeSeconds()
            : 0;
    }
}
