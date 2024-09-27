using System.Text.Json.Serialization;

namespace FrigateBot.Model;

public sealed class EventModel
{
    public required string Camera { get; init; }

    public EventDataModel? Data { get; init; }

    [JsonPropertyName("start_time")]
    [JsonConverter(typeof(EpochTimeJsonConverter))]
    public DateTimeOffset StartTime { get; init; }

    [JsonPropertyName("end_time")]
    [JsonConverter(typeof(EpochTimeJsonConverter))]
    public DateTimeOffset EndTime { get; init; }

    [JsonPropertyName("has_clip")]
    public bool HasClip { get; init; }

    [JsonPropertyName("has_snapshot")]
    public bool HasSnapshot { get; init; }

    public required string Id { get; init; }

    public required string Label { get; init; }

    public required List<string> Zones { get; init; }

    public string Thumbnail { get; init; } = "";
}
