using System.Text.Json.Serialization;
using PinballScores.Core.Models;

namespace PinballScores.Core.Api;

/// <summary>One score in a submission batch.</summary>
public sealed class ScoreSubmission
{
    [JsonPropertyName("table")]
    public required string Table { get; init; }

    /// <summary>Null means the main ranked leaderboard.</summary>
    [JsonPropertyName("category")]
    public string? Category { get; init; }

    [JsonPropertyName("initials")]
    public required string Initials { get; init; }

    /// <summary>Sent as a string so values above 2^53 survive JSON intact.</summary>
    [JsonPropertyName("value")]
    public required string Value { get; init; }

    [JsonPropertyName("value_type")]
    public required string ValueType { get; init; }

    [JsonPropertyName("display_suffix")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplaySuffix { get; init; }

    public static ScoreSubmission From(ScoreEntry entry) => new()
    {
        Table = entry.Table,
        Category = entry.Category,
        Initials = entry.Player,
        Value = entry.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ValueType = entry.ValueKind switch
        {
            ScoreValueKind.Counter => "counter",
            ScoreValueKind.Duration => "duration",
            ScoreValueKind.Timestamp => "timestamp",
            _ => "score",
        },
        DisplaySuffix = entry.DisplaySuffix,
    };
}

internal sealed class SubmitRequest
{
    [JsonPropertyName("scores")]
    public required IReadOnlyList<ScoreSubmission> Scores { get; init; }
}

/// <summary>Per-entry outcome. The API never fails a whole batch for one bad row.</summary>
public sealed class ScoreResult
{
    [JsonPropertyName("index")] public int Index { get; init; }
    [JsonPropertyName("status")] public string Status { get; init; } = "";
    [JsonPropertyName("table")] public string? Table { get; init; }
    [JsonPropertyName("category")] public string? Category { get; init; }
    [JsonPropertyName("initials")] public string? Initials { get; init; }
    [JsonPropertyName("value")] public string? Value { get; init; }
    [JsonPropertyName("reason")] public string? Reason { get; init; }

    public bool WasInserted => Status == "inserted";
    public bool WasRejected => Status == "rejected";
}

/// <summary>
/// Batch outcome. Duplicates are the expected case: the extractor resubmits the
/// same visible board on most runs and the API deduplicates.
/// </summary>
public sealed class SubmitResponse
{
    [JsonPropertyName("received")] public int Received { get; init; }
    [JsonPropertyName("inserted")] public int Inserted { get; init; }
    [JsonPropertyName("duplicates")] public int Duplicates { get; init; }
    [JsonPropertyName("rejected")] public int Rejected { get; init; }
    [JsonPropertyName("new_tables")] public IReadOnlyList<string> NewTables { get; init; } = [];
    [JsonPropertyName("results")] public IReadOnlyList<ScoreResult> Results { get; init; } = [];
}

/// <summary>A score as the API currently holds it, used to drive write-back.</summary>
public sealed class RemoteScore
{
    [JsonPropertyName("table")] public string Table { get; init; } = "";
    [JsonPropertyName("category")] public string? Category { get; init; }
    [JsonPropertyName("label")] public string? Label { get; init; }
    [JsonPropertyName("initials")] public string Initials { get; init; } = "";
    [JsonPropertyName("value")] public string Value { get; init; } = "0";
    [JsonPropertyName("value_type")] public string? ValueType { get; init; }
    [JsonPropertyName("display_suffix")] public string? DisplaySuffix { get; init; }
    [JsonPropertyName("rank")] public int Rank { get; init; }

    public long AsInt64 => long.TryParse(Value, out var v) ? v : 0;
}
