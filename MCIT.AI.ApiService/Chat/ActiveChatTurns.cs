using System.Collections.Concurrent;

namespace MCIT.AI.ApiService.Chat;

internal sealed class ActiveChatTurns
{
    private readonly ConcurrentDictionary<(Guid Owner, Guid Turn), CancellationTokenSource> turns = new();

    public bool TryAdd(Guid owner, Guid turn, CancellationTokenSource cancellation) =>
        turns.TryAdd((owner, turn), cancellation);

    public void Remove(Guid owner, Guid turn) => turns.TryRemove((owner, turn), out _);

    public bool TryStop(Guid owner, Guid turn)
    {
        if (!turns.TryGetValue((owner, turn), out CancellationTokenSource? cancellation))
        {
            return false;
        }

        try
        {
            cancellation.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }
}
