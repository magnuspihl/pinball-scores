using System.Globalization;
using System.Text.Json;

namespace PinballScores.Core.Nvram;

/// <summary>
/// Helpers for the loose typing in the map format: addresses may be decimal
/// numbers or "0x..." strings, and several fields are optional everywhere.
/// </summary>
internal static class MapJson
{
    public static JsonElement? Prop(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : null;

    /// <summary>Reads an address or count that may be written as a number or a hex string.</summary>
    public static long? Number(this JsonElement? e)
    {
        if (e is not { } v) return null;
        switch (v.ValueKind)
        {
            case JsonValueKind.Number:
                return v.GetInt64();
            case JsonValueKind.String:
                var s = v.GetString();
                if (string.IsNullOrWhiteSpace(s)) return null;
                s = s.Trim();
                if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    return long.Parse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                return long.Parse(s, CultureInfo.InvariantCulture);
            default:
                return null;
        }
    }

    public static double? Double(this JsonElement? e) => e switch
    {
        { ValueKind: JsonValueKind.Number } v => v.GetDouble(),
        { ValueKind: JsonValueKind.String } v when double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
        _ => null,
    };

    public static string? Str(this JsonElement? e) =>
        e is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

    public static long[]? Longs(this JsonElement? e)
    {
        if (e is not { ValueKind: JsonValueKind.Array } v) return null;
        var list = new List<long>();
        foreach (var item in v.EnumerateArray())
        {
            var n = ((JsonElement?)item).Number();
            if (n is { } value) list.Add(value);
        }
        return list.Count == 0 ? null : list.ToArray();
    }
}
