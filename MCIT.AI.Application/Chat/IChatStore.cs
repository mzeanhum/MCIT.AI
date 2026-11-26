namespace MCIT.AI.Application.Chat;

public sealed record ConversationSummary(Guid Id, string Title);
public sealed record SavedMemory(Guid Id, string Content);

public interface IChatStore
{
    Task<List<ConversationSummary>> ListAsync(Guid owner, CancellationToken cancellationToken);
    Task<List<ChatMessage>?> ReadAsync(Guid owner, Guid conversation, CancellationToken cancellationToken);
    Task<bool> SaveTurnAsync(Guid owner, Guid conversation, int expectedCount, string prompt, string reply, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid owner, Guid conversation, CancellationToken cancellationToken);
    Task<List<SavedMemory>> ListMemoryAsync(Guid owner, CancellationToken cancellationToken);
    Task<SavedMemory> AddMemoryAsync(Guid owner, string content, CancellationToken cancellationToken);
    Task<bool> DeleteMemoryAsync(Guid owner, Guid id, CancellationToken cancellationToken);
}
