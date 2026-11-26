-- Run against MCITChatBot using a database migration account.
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> N'MCITChatBot'
    THROW 50001, 'This migration targets the MCITChatBot database.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @lockResult int;
    EXEC @lockResult = sys.sp_getapplock
        @Resource = N'MCIT.AI.Chat.SchemaMigration',
        @LockMode = 'Exclusive',
        @LockOwner = 'Transaction',
        @LockTimeout = 10000;

    IF @lockResult < 0
        THROW 50002, 'Could not obtain the chat schema migration lock.', 1;

    IF SCHEMA_ID(N'chat') IS NULL
        EXEC(N'CREATE SCHEMA chat AUTHORIZATION dbo;');

    IF OBJECT_ID(N'chat.SchemaMigrations', N'U') IS NULL
        CREATE TABLE chat.SchemaMigrations
        (
            MigrationId int NOT NULL CONSTRAINT PK_ChatSchemaMigrations PRIMARY KEY,
            AppliedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_ChatSchemaMigrations_Applied DEFAULT SYSUTCDATETIME()
        );

    IF NOT EXISTS (SELECT 1 FROM chat.SchemaMigrations WHERE MigrationId = 1)
    BEGIN
        CREATE TABLE chat.Conversations
        (
            ConversationId uniqueidentifier NOT NULL CONSTRAINT PK_ChatConversations PRIMARY KEY,
            OwnerId uniqueidentifier NOT NULL,
            Title nvarchar(200) NOT NULL,
            CreatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_ChatConversations_Created DEFAULT SYSUTCDATETIME(),
            UpdatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_ChatConversations_Updated DEFAULT SYSUTCDATETIME()
        );

        CREATE INDEX IX_ChatConversations_Owner_Updated
            ON chat.Conversations (OwnerId, UpdatedAtUtc DESC, ConversationId);

        CREATE TABLE chat.Messages
        (
            ConversationId uniqueidentifier NOT NULL,
            SequenceNumber int NOT NULL,
            Role varchar(16) NOT NULL,
            Content nvarchar(max) NOT NULL,
            CreatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_ChatMessages_Created DEFAULT SYSUTCDATETIME(),
            CONSTRAINT PK_ChatMessages PRIMARY KEY (ConversationId, SequenceNumber),
            CONSTRAINT FK_ChatMessages_Conversation FOREIGN KEY (ConversationId)
                REFERENCES chat.Conversations (ConversationId) ON DELETE CASCADE,
            CONSTRAINT CK_ChatMessages_Sequence CHECK (SequenceNumber >= 1),
            CONSTRAINT CK_ChatMessages_Role CHECK (Role IN ('user', 'assistant'))
        );

        CREATE TABLE chat.MemoryNotes
        (
            MemoryId uniqueidentifier NOT NULL CONSTRAINT PK_ChatMemoryNotes PRIMARY KEY,
            OwnerId uniqueidentifier NOT NULL,
            Content nvarchar(2000) NOT NULL,
            CreatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_ChatMemoryNotes_Created DEFAULT SYSUTCDATETIME(),
            UpdatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_ChatMemoryNotes_Updated DEFAULT SYSUTCDATETIME(),
            CONSTRAINT CK_ChatMemoryNotes_Content CHECK (LEN(Content) > 0)
        );

        CREATE INDEX IX_ChatMemoryNotes_Owner_Updated
            ON chat.MemoryNotes (OwnerId, UpdatedAtUtc DESC, MemoryId);

        INSERT chat.SchemaMigrations (MigrationId) VALUES (1);
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
