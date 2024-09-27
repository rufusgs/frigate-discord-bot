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

    public void Dispose()
    {
        httpClient.Dispose();
    }
}
