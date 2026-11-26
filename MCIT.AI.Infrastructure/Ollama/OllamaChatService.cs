using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MCIT.AI.Application.Chat;
using Microsoft.Extensions.Options;

namespace MCIT.AI.Infrastructure.Ollama;

public sealed class OllamaChatService(HttpClient httpClient, IOptions<OllamaOptions> options) : IChatService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async IAsyncEnumerable<string> StreamReplyAsync(
        IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using CancellationTokenSource firstReply = new(TimeSpan.FromSeconds(options.Value.FirstReplyTimeoutSeconds));
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, firstReply.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
        CancellationToken token = deadline.Token;
        using HttpRequestMessage request = new(HttpMethod.Post, "api/chat")
        {
            Content = JsonContent.Create(new { model = options.Value.Model, messages, stream = true, think = options.Value.Think, options = new { num_ctx = options.Value.ContextLength } })
        };
        using HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        using StreamReader reader = new(await response.Content.ReadAsStreamAsync(token));

        bool answerStarted = false;
        while (await reader.ReadLineAsync(token) is string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            OllamaChunk chunk = JsonSerializer.Deserialize<OllamaChunk>(line, JsonOptions)
                ?? throw new InvalidDataException("Invalid Ollama stream response.");
            if (!string.IsNullOrEmpty(chunk.Error))
            {
                throw new InvalidDataException("Ollama reported a streaming error.");
            }
            if (chunk.Message?.Content is { Length: > 0 } text)
            {
                if (!answerStarted && !string.IsNullOrWhiteSpace(text))
                {
                    firstReply.CancelAfter(Timeout.InfiniteTimeSpan);
                    answerStarted = true;
                }

                yield return text;
            }
            if (chunk.Done)
            {
                yield break;
            }
        }

        throw new EndOfStreamException("Ollama ended the response before completion.");
    }

    private sealed record OllamaChunk(ChatMessage? Message, bool Done, string? Error);
}
