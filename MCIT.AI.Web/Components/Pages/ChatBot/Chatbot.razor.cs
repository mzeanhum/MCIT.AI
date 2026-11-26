using System.Text;
using System.Text.Json;
using MCIT.AI.Application.Chat;
using MCIT.AI.Web.Chat;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace MCIT.AI.Web.Components.Pages.ChatBot;

public partial class Chatbot : IDisposable
{
    [Inject] private ChatApiClient ChatClient { get; set; } = default!;

    [Inject] private AuthenticationStateProvider BrowserIdentity { get; set; } = default!;

    private readonly CancellationTokenSource lifetime = new();
    private Guid conversationId = Guid.NewGuid();
    private string? owner;
    private bool hasSavedConversation;
    private bool storageBusy;
    private bool showHistory;
    private bool showMemory;
    private List<ConversationSummary>? history;
    private List<SavedMemory> memory = [];
    private string memoryDraft = string.Empty;
    private string? storageError;

    private bool isSending => responseCancellation is not null;
    private Guid? activeTurnId;
    private bool stopping;
    private string? notice;
    private string? error;
    private string input = string.Empty;
    private readonly List<ChatMessage> messages = [];
    private CancellationTokenSource? responseCancellation;
    private bool disposed;

    private async Task SendAsync()
    {
        if (isSending || storageBusy || owner is null || string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        string prompt = input.Trim();
        if (prompt.Length > 32_000)
        {
            error = "Use a message of no more than 32000 characters.";
            return;
        }

        using CancellationTokenSource cancellation = new();
        responseCancellation = cancellation;
        Guid turnId = Guid.NewGuid();
        activeTurnId = turnId;
        notice = null;
        input = string.Empty;
        error = null;
        int turnStart = messages.Count;
        messages.Add(new ChatMessage("user", prompt));
        ChatMessage[] request = history is null ? messages.TakeLast(128).ToArray() : [messages[^1]];
        StringBuilder reply = new();
        bool refreshHistory = false;
        try
        {
            await foreach (ChatStreamUpdate update in ChatClient.StreamReplyAsync(request, owner, conversationId,
                               !hasSavedConversation, turnId, cancellation.Token))
            {
                if (disposed)
                {
                    return;
                }

                if (update.Stopped)
                {
                    notice = "Response stopped.";
                    continue;
                }

                reply.Append(update.Text);

                ChatMessage assistant = new("assistant", reply.ToString());
                if (messages.Count == turnStart + 1)
                {
                    messages.Add(assistant);
                }
                else
                {
                    messages[^1] = assistant;
                }

                StateHasChanged();
            }
            hasSavedConversation = true;
            refreshHistory = true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Leaving the page cancels the connection, while the Stop button uses the API.
        }
        catch (ChatResponseException ex)
        {
            RestoreDraft(turnStart, prompt);
            error = ex.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or OperationCanceledException)
        {
            RestoreDraft(turnStart, prompt);
            error = "The chat response could not finish. Check that ApiService and the configured model are running, then retry.";
        }
        finally
        {
            responseCancellation = null;
            activeTurnId = null;
            stopping = false;
        }

        if (refreshHistory && !disposed)
        {
            await RefreshHistoryAsync();
        }
    }
    private void ToggleHistory() => showHistory = !showHistory;

    private void RestoreDraft(int turnStart, string prompt)
    {
        messages.RemoveRange(turnStart, messages.Count - turnStart);
        input = prompt;
    }

    private async Task StopResponseAsync()
    {
        if (owner is null || activeTurnId is not Guid turnId || stopping)
        {
            return;
        }

        stopping = true;
        try
        {
            if (!await ChatClient.StopReplyAsync(turnId, owner, lifetime.Token) && activeTurnId == turnId)
            {
                error = "The stop request did not reach the current response. Try again.";
            }
        }
        catch (OperationCanceledException) when (disposed)
        {
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            if (activeTurnId == turnId)
            {
                error = "Could not stop the current response. Try again.";
            }
        }
        finally
        {
            if (activeTurnId == turnId)
            {
                stopping = false;
            }
        }
    }

    private void NewChat()
    {
        if (isSending || storageBusy)
        {
            return;
        }

        input = string.Empty;
        messages.Clear();
        conversationId = Guid.NewGuid();
        hasSavedConversation = false;
        showHistory = false;
        error = null;
        notice = null;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            owner = (await BrowserIdentity.GetAuthenticationStateAsync()).User.FindFirst(ChatBrowserIdentity.ClaimType)?.Value;
            if (owner is null)
            {
                error = "Reload the chat page to initialize your browser session.";
            }
            await RefreshHistoryAsync();
            if (!disposed)
            {
                StateHasChanged();
            }
        }
    }

    private Task RefreshHistoryAsync() => RunStorageAsync(async (key, token) =>
    {
        history = await ChatClient.ListHistoryAsync(key, token);
        if (history is null)
        {
            storageError = "Chat history is unavailable.";
        }
    });

    private async Task ToggleMemoryAsync()
    {
        showMemory = !showMemory;
        if (showMemory)
        {
            await RunStorageAsync(async (key, token) =>
            {
                List<SavedMemory>? notes = await ChatClient.ListMemoryAsync(key, token);
                memory = notes ?? [];
                if (notes is null)
                {
                    storageError = "Saved memory is unavailable.";
                }
            });
        }
    }

    private Task OpenConversationAsync(Guid id) => RunStorageAsync(async (key, token) =>
    {
        List<ChatMessage>? saved = await ChatClient.LoadHistoryAsync(id, key, token);
        if (saved is null)
        {
            storageError = "This conversation is no longer available.";
            return;
        }

        messages.Clear();
        messages.AddRange(saved);
        conversationId = id;
        hasSavedConversation = true;
        input = string.Empty;
        error = null;
        notice = null;
        showHistory = false;
    });

    private Task DeleteConversationAsync(Guid id) => RunStorageAsync(async (key, token) =>
    {
        await ChatClient.DeleteHistoryAsync(id, key, token);
        history?.RemoveAll(item => item.Id == id);
        if (conversationId == id)
        {
            messages.Clear();
            conversationId = Guid.NewGuid();
            hasSavedConversation = false;
            error = null;
        }
    });

    private Task AddMemoryAsync() => RunStorageAsync(async (key, token) =>
    {
        if (string.IsNullOrWhiteSpace(memoryDraft) || memory.Count >= 20)
        {
            return;
        }

        await ChatClient.AddMemoryAsync(memoryDraft.Trim(), key, token);
        memoryDraft = string.Empty;
        memory = await ChatClient.ListMemoryAsync(key, token) ?? [];
    });

    private Task DeleteMemoryAsync(Guid id) => RunStorageAsync(async (key, token) =>
    {
        await ChatClient.DeleteMemoryAsync(id, key, token);
        memory.RemoveAll(item => item.Id == id);
    });

    private async Task RunStorageAsync(Func<string, CancellationToken, Task> action)
    {
        if (owner is null || storageBusy || isSending)
        {
            return;
        }
        storageBusy = true;
        storageError = null;
        try
        {
            await action(owner, lifetime.Token);
        }
        catch (OperationCanceledException) when (disposed)
        {
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or IOException or OperationCanceledException)
        {
            storageError = "Chat storage could not complete the request. Try again.";
        }
        finally
        {
            storageBusy = false;
        }
    }

    public void Dispose()
    {
        disposed = true;
        responseCancellation?.Cancel();
        lifetime.Cancel();
        lifetime.Dispose();
    }
}
