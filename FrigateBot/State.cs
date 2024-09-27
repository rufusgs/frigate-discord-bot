using System.Text.Json;

namespace FrigateBot;

public sealed class State
{
    private string path = "";

    public static State Load(string path)
    {
        State result;
        if (File.Exists(path))
        {
            using var file = File.OpenRead(path);
            result = JsonSerializer.Deserialize<State>(file) ?? new();
        }
        else
        {
            result = new();
            result.Save(path);
        }
        result.path = path;
        return result;
    }

    public void Save(string? path = null)
    {
        using var file = File.Create(path ?? this.path);
        JsonSerializer.Serialize(file, this);

        if (path is not null)
        {
            this.path = path;
        }
    }

    public void Alter(Action<State> action)
    {
        action(this);
        Save();
    }

    public DateTimeOffset? LastCompletedEventStartUtc { get; set; }
    public Dictionary<ulong, ulong> ConfigureCommandByGuildId { get; init; } = [];
    public Dictionary<ulong, ulong> CctvChannelByGuild { get; init; } = [];
}
