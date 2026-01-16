using System.Text.Json.Serialization;

namespace FrigateBot.Model;

public sealed class EventDataModel
{
    public float[]? Box { get; init; }
    public float[]? Region { get; init; }
    public float? Score { get; init; }
    [JsonPropertyName("top_score")]
    public float? TopScore { get; init; }
    public string? Type { get; init; }
    [JsonPropertyName("max_severity")]
    public string? MaxSeverity { get; init; }
}
