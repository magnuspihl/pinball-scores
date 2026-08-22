using System.Text.Json;
using PinballScores.Core.Models;

namespace PinballScores.Core.Nvram;

/// <summary>
/// A board on a machine, as declared by the map's <c>_pinballscores.categories</c>
/// block: which physical slots belong to it, and how its values should be read.
///
/// The map is authoritative here. Category identity used to be derived from slot
/// labels with string rules, which meant the CLI and the maps could disagree about
/// what a category was called.
/// </summary>
public sealed record CategoryDefinition(
    string Key,
    string? Name,
    IReadOnlyList<string> Slots,
    ScoreValueKind ValueKind)
{
    /// <summary>
    /// The machine's main ranked leaderboard. Exactly one category per map has a
    /// null name — the maps use the key "main" for it.
    /// </summary>
    public bool IsMainBoard => Name is null;

    /// <summary>
    /// What gets submitted as the category. The stable key rather than the display
    /// label, so labels can be changed on the website without splitting a category
    /// or rewriting stored rows. The main board is null, as the API expects.
    /// </summary>
    public string? ApiCategory => IsMainBoard ? null : Key;

    /// <summary>
    /// Whether a category name the API returned refers to this category.
    ///
    /// Deliberately not an equality check. The API keys a category by the string it
    /// was first submitted with, and every row on the server predating this CLI was
    /// loaded under the ROM's own label in upper case ("CASTLE CHAMPION"), while a
    /// map identifies the same board by its key ("castle_champion"). Comparing the
    /// two literally matched nothing but single-word categories, so write-back saw
    /// no rows for a category and filled its slots with the blanking placeholder —
    /// Medieval Madness lost all eleven of its champion slots that way.
    ///
    /// Case and separators are therefore ignored on both sides, and the display name
    /// is accepted as well as the key. Neither spelling is ambiguous: a key is the
    /// slug of its own name, and no two categories in a map differ only by
    /// punctuation.
    /// </summary>
    public bool Matches(string? apiCategory)
    {
        // The API sends null for the main board; nothing else may claim those rows.
        if (string.IsNullOrWhiteSpace(apiCategory)) return IsMainBoard;
        if (IsMainBoard) return false;

        var canonical = Canonical(apiCategory);
        return canonical == Canonical(Key) || (Name is not null && canonical == Canonical(Name));
    }

    /// <summary>Comparable form of a category name: letters and digits, upper case.</summary>
    private static string Canonical(string value) =>
        string.Concat(value.Where(char.IsLetterOrDigit)).ToUpperInvariant();

    public static List<CategoryDefinition> Parse(JsonElement? root)
    {
        var categories = new List<CategoryDefinition>();
        var block = root?.Prop("_pinballscores")?.Prop("categories");
        if (block is not { ValueKind: JsonValueKind.Array } array) return categories;

        foreach (var entry in array.EnumerateArray())
        {
            var key = entry.Prop("key").Str();
            if (key is null) continue;

            var slots = new List<string>();
            if (entry.Prop("slots") is { ValueKind: JsonValueKind.Array } slotArray)
                foreach (var slot in slotArray.EnumerateArray())
                    if (slot.GetString() is { } label) slots.Add(label);

            categories.Add(new CategoryDefinition(
                key,
                entry.Prop("name").Str(),
                slots,
                ParseValueKind(entry.Prop("value_type").Str())));
        }

        return categories;
    }

    private static ScoreValueKind ParseValueKind(string? value) => value?.ToLowerInvariant() switch
    {
        "counter" => ScoreValueKind.Counter,
        "duration" => ScoreValueKind.Duration,
        "timestamp" => ScoreValueKind.Timestamp,
        _ => ScoreValueKind.Score,
    };
}
