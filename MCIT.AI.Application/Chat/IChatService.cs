namespace MCIT.AI.Application.Chat;

public sealed record ChatMessage(string Role, string Content);

public interface IChatService
{
    IAsyncEnumerable<string> StreamReplyAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default);
}
