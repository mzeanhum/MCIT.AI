using System.Net;
using System.Net.Http.Headers;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MCIT.AI.Application.Chat;

namespace MCIT.AI.Web.Chat;

internal sealed class ChatApiClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async IAsyncEnumerable<ChatStreamUpdate> StreamReplyAsync(
        IReadOnlyList<ChatMessage> messages, string owner, Guid conversationId, bool isNew, Guid turnId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "api/chat/stream")
        {
            Content = JsonContent.Create(new { messages, conversationId, isNew, turnId })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("ChatOwner", owner);
        using HttpResponseMessage response = await httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken);
        await foreach (SseItem<string> item in SseParser.Create(body).EnumerateAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (item.EventType)
            {
                case "text.delta":
                    ChatEvent delta = JsonSerializer.Deserialize<ChatEvent>(item.Data, JsonOptions)
                        ?? throw new InvalidDataException("Invalid chat event.");
                    yield return new ChatStreamUpdate(delta.Text ?? throw new InvalidDataException("Missing chat text."), false);
                    break;
                case "stopped":
                    yield return new ChatStreamUpdate(null, true);
                    yield break;
                case "completed":
                    yield break;
                case "error":
                    ChatEvent? failure = JsonSerializer.Deserialize<ChatEvent>(item.Data, JsonOptions);
                    throw new ChatResponseException(failure?.Text ?? "The chat response could not finish.");
            }
        }

        throw new EndOfStreamException("Chat API closed the stream before completion.");
    }

    public async Task<bool> StopReplyAsync(Guid turnId, string owner, CancellationToken token)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            using HttpResponseMessage response = await SendStorageAsync(HttpMethod.Post, $"api/chat/turns/{turnId}/stop", owner, null, token);
            if (response.StatusCode != HttpStatusCode.NotFound)
            {
                response.EnsureSuccessStatusCode();
                return true;
            }
            // The user may press Stop before the server has registered the stream.
            await Task.Delay(TimeSpan.FromMilliseconds(200), token);
        }

        return false;
    }

    public Task<List<ConversationSummary>?> ListHistoryAsync(string owner, CancellationToken token) =>
        ReadAsync<List<ConversationSummary>>("api/chat/history", owner, token);

    public Task<List<ChatMessage>?> LoadHistoryAsync(Guid id, string owner, CancellationToken token) =>
        ReadAsync<List<ChatMessage>>($"api/chat/history/{id}", owner, token);

    public Task<List<SavedMemory>?> ListMemoryAsync(string owner, CancellationToken token) =>
        ReadAsync<List<SavedMemory>>("api/chat/memory", owner, token);

    public async Task DeleteHistoryAsync(Guid id, string owner, CancellationToken token)
    {
        using HttpResponseMessage response = await SendStorageAsync(HttpMethod.Delete, $"api/chat/history/{id}", owner, null, token);
        response.EnsureSuccessStatusCode();
    }

    public async Task AddMemoryAsync(string content, string owner, CancellationToken token)
    {
        using HttpResponseMessage response = await SendStorageAsync(HttpMethod.Post, "api/chat/memory", owner, JsonContent.Create(new { content }), token);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteMemoryAsync(Guid id, string owner, CancellationToken token)
    {
        using HttpResponseMessage response = await SendStorageAsync(HttpMethod.Delete, $"api/chat/memory/{id}", owner, null, token);
        response.EnsureSuccessStatusCode();
    }

    private async Task<T?> ReadAsync<T>(string path, string owner, CancellationToken token)
    {
        using HttpResponseMessage response = await SendStorageAsync(HttpMethod.Get, path, owner, null, token);
        if (response.StatusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.NotFound)
        {
            return default;
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: token);
    }

    private async Task<HttpResponseMessage> SendStorageAsync(HttpMethod method, string path, string owner, HttpContent? content,
        CancellationToken token)
    {
        using HttpRequestMessage request = new(method, path) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("ChatOwner", owner);
        return await httpClient.SendAsync(request, token);
    }

    private sealed record ChatEvent(string? Text);
}

internal sealed record ChatStreamUpdate(string? Text, bool Stopped);

internal sealed class ChatResponseException(string message) : IOException(message);
