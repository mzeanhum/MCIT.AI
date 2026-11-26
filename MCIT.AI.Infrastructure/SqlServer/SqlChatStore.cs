using System.Data;
using MCIT.AI.Application.Chat;
using Microsoft.Data.SqlClient;

namespace MCIT.AI.Infrastructure.SqlServer;

public sealed class SqlChatStore(string connectionString) : IChatStore
{
    public Task<List<ConversationSummary>> ListAsync(Guid owner, CancellationToken cancellationToken) =>
        QueryAsync("SELECT TOP (100) ConversationId, Title FROM chat.Conversations WHERE OwnerId = @owner ORDER BY UpdatedAtUtc DESC, ConversationId;",
            owner, null, reader => new ConversationSummary(reader.GetGuid(0), reader.GetString(1)), cancellationToken);

    public async Task<List<ChatMessage>?> ReadAsync(Guid owner, Guid conversation, CancellationToken cancellationToken)
    {
        List<ChatMessage> messages = await QueryAsync("""
            SELECT m.Role, m.Content FROM chat.Conversations c
            JOIN chat.Messages m ON m.ConversationId = c.ConversationId
            WHERE c.OwnerId = @owner AND c.ConversationId = @id
            ORDER BY m.SequenceNumber;
            """, owner, conversation, reader => new ChatMessage(reader.GetString(0), reader.GetString(1)), cancellationToken);
        return messages.Count == 0 ? null : messages;
    }

    public async Task<bool> SaveTurnAsync(Guid owner, Guid conversation, int expectedCount, string prompt, string reply,
        CancellationToken cancellationToken)
    {
        await using SqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using SqlCommand command = new("""
            DECLARE @actualOwner uniqueidentifier;
            SELECT @actualOwner = OwnerId FROM chat.Conversations WITH (UPDLOCK, HOLDLOCK)
            WHERE ConversationId = @id;

            IF @actualOwner IS NULL AND @expected = 0
                INSERT chat.Conversations (ConversationId, OwnerId, Title) VALUES (@id, @owner, @title);
            ELSE IF @actualOwner IS NULL OR @actualOwner <> @owner
                BEGIN SELECT CAST(0 AS bit); RETURN; END;

            IF @expected <> (SELECT COUNT(*) FROM chat.Messages WHERE ConversationId = @id)
                BEGIN SELECT CAST(0 AS bit); RETURN; END;

            INSERT chat.Messages (ConversationId, SequenceNumber, Role, Content)
            VALUES (@id, @expected + 1, 'user', @prompt);
            IF DATALENGTH(@reply) > 0
                INSERT chat.Messages (ConversationId, SequenceNumber, Role, Content)
                VALUES (@id, @expected + 2, 'assistant', @reply);
            UPDATE chat.Conversations SET UpdatedAtUtc = SYSUTCDATETIME() WHERE ConversationId = @id AND OwnerId = @owner;
            SELECT CAST(1 AS bit);
            """, connection, transaction);
        AddIds(command, owner, conversation);
        command.Parameters.Add("@expected", SqlDbType.Int).Value = expectedCount;
        command.Parameters.Add("@title", SqlDbType.NVarChar, 200).Value = prompt.Length > 200 ? prompt[..199] + "…" : prompt;
        command.Parameters.Add("@prompt", SqlDbType.NVarChar, -1).Value = prompt;
        command.Parameters.Add("@reply", SqlDbType.NVarChar, -1).Value = reply;
        if (await command.ExecuteScalarAsync(cancellationToken) is not true)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public Task<bool> DeleteAsync(Guid owner, Guid conversation, CancellationToken cancellationToken) =>
        ExecuteAsync("DELETE FROM chat.Conversations WHERE OwnerId = @owner AND ConversationId = @id;",
            owner, conversation, null, cancellationToken);

    public Task<List<SavedMemory>> ListMemoryAsync(Guid owner, CancellationToken cancellationToken) =>
        QueryAsync("SELECT TOP (20) MemoryId, Content FROM chat.MemoryNotes WHERE OwnerId = @owner ORDER BY UpdatedAtUtc DESC, MemoryId;",
            owner, null, reader => new SavedMemory(reader.GetGuid(0), reader.GetString(1)), cancellationToken);

    public async Task<SavedMemory> AddMemoryAsync(Guid owner, string content, CancellationToken cancellationToken)
    {
        SavedMemory note = new(Guid.NewGuid(), content);
        bool inserted = await ExecuteAsync("""
            INSERT chat.MemoryNotes (OwnerId, MemoryId, Content)
            SELECT @owner, @id, @content
            WHERE (SELECT COUNT(*) FROM chat.MemoryNotes WITH (UPDLOCK, HOLDLOCK) WHERE OwnerId = @owner) < 20;
            """, owner, note.Id, content, cancellationToken);
        return inserted ? note : throw new InvalidOperationException("A maximum of 20 memory notes can be saved.");
    }

    public Task<bool> DeleteMemoryAsync(Guid owner, Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync("DELETE FROM chat.MemoryNotes WHERE OwnerId = @owner AND MemoryId = @id;",
            owner, id, null, cancellationToken);

    private async Task<List<T>> QueryAsync<T>(string sql, Guid owner, Guid? id, Func<SqlDataReader, T> map, CancellationToken token)
    {
        await using SqlConnection connection = new(connectionString);
        await connection.OpenAsync(token);
        await using SqlCommand command = new(sql, connection);
        AddIds(command, owner, id);
        await using SqlDataReader reader = await command.ExecuteReaderAsync(token);
        List<T> result = [];
        while (await reader.ReadAsync(token))
        {
            result.Add(map(reader));
        }

        return result;
    }

    private async Task<bool> ExecuteAsync(string sql, Guid owner, Guid id, string? content, CancellationToken token)
    {
        await using SqlConnection connection = new(connectionString);
        await connection.OpenAsync(token);
        await using SqlCommand command = new(sql, connection);
        AddIds(command, owner, id);
        if (content is not null)
        {
            command.Parameters.Add("@content", SqlDbType.NVarChar, 2000).Value = content;
        }

        return await command.ExecuteNonQueryAsync(token) > 0;
    }

    private static void AddIds(SqlCommand command, Guid owner, Guid? id)
    {
        command.Parameters.Add("@owner", SqlDbType.UniqueIdentifier).Value = owner;
        if (id.HasValue)
        {
            command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id.Value;
        }
    }
}
