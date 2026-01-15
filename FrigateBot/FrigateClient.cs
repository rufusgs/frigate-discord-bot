using FrigateBot.Model;
using System.Net.Http.Json;
using System.Text;

namespace FrigateBot;

public sealed class FrigateClient(string baseAddress) : IDisposable
{
    private readonly HttpClient httpClient = new() { BaseAddress = new(baseAddress) };

    public Task<List<EventModel>?> GetEventsAsync(DateTimeOffset? since)
    {
        var queryBuilder = new StringBuilder("api/events?sort=date_asc");

        if (since.HasValue)
        {
            queryBuilder.Append("&after=");
            queryBuilder.Append(EpochTimeJsonConverter.ToEpochTime(since.Value));
        }

        return httpClient.GetFromJsonAsync<List<EventModel>>(queryBuilder.ToString());
    }

    public async Task<Stream?> GetEventThumbnailAsync(string eventId, string extension)
    {
        var queryBuilder = new StringBuilder("api/events/");
        queryBuilder.Append(eventId);
        queryBuilder.Append("/thumbnail.");
        queryBuilder.Append(extension);

        return await httpClient.GetStreamAsync(queryBuilder.ToString());
    }

    public async Task<Stream?> GetEventPreviewAsync(string eventId)
    {
        var queryBuilder = new StringBuilder("api/events/");
        queryBuilder.Append(eventId);
        queryBuilder.Append("/preview.gif");

        return await httpClient.GetStreamAsync(queryBuilder.ToString());
    }

    public async Task<Stream?> GetEventClipAsync(string eventId)
    {
        var queryBuilder = new StringBuilder("api/events/");
        queryBuilder.Append(eventId);
        queryBuilder.Append("/clip.mp4");

        return await httpClient.GetStreamAsync(queryBuilder.ToString());
    }

    public void Dispose()
    {
        httpClient.Dispose();
    }
}
