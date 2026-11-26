# MCIT.AI solution

Aspire starts the Blazor frontend and backend API as local .NET processes. Start Ollama separately, run AppHost from Visual Studio, and open the webfrontend endpoint, then open Chatbot. Docker is optional for future resources and is unnecessary for the current solution.

| Project | Responsibility |
| --- | --- |
| Web | Blazor UI, interactive session state and composition of services used by pages |
| Application | Feature contracts and use cases, organized by capability |
| Infrastructure | Ollama and future adapters for storage, search, media and external systems |
| ApiService | Backend entry point for chat and future features |
| AppHost | Local process orchestration and resource health |
| ServiceDefaults | Shared telemetry, service discovery, health checks and HTTP defaults |

The chat path is Web → ApiService → Application contract ← Infrastructure. ApiService registers the Ollama implementation. Application has no dependency on Aspire, ASP.NET Core or Ollama. The API streams Ollama replies to Blazor over server-sent events and saves completed turns when SQL storage is configured. The send handler is in Chatbot.razor.cs for easier C# debugging.

## Configuration

- Directory.Build.props sets the shared .NET target and compiler defaults. Directory.Packages.props is the source of truth for explicit NuGet versions. The AppHost SDK version is declared in AppHost's project file.
- MCIT.AI.ApiService/appsettings.json contains the Ollama endpoint, model and timeout. The default model is qwen3.5:latest, the endpoint is http://localhost:11434/ and the timeout is 300 seconds, and the Ollama context defaults to 8192 tokens. The host validates these values at startup. Use Ollama__Endpoint, Ollama__Model, Ollama__TimeoutSeconds and Ollama__ContextLength for environment overrides.
- Each executable owns its environment settings and secrets. AppHost has its own Visual Studio launch profiles and address fallbacks for launches that omit a profile. Keep provider credentials out of tracked settings.
- Ollama requests use their configured timeout without automatic retries. Keep provider-specific policies inside adapters.

## Growth plan

| Capability | Where it belongs | First implementation |
| --- | --- | --- |
| Conversations and identity | Application use cases, Infrastructure persistence | Add signed-in conversation ownership, then save history. Enforce ownership on read and write. |
| MCP and tools | Application defines permitted tool actions, Infrastructure implements MCP connections | Start with one named, read-only tool. Check permissions independently of model output. Require approval and audit for actions that change external state. |
| Microphone and images | Web captures audio and previews images, Application coordinates requests, Infrastructure calls media providers | Add bounded uploads, validate content and size on the server, and define file retention before storing media. |
| Document search | Documents feature owns ingestion and retrieval, Infrastructure owns parsers, index and storage | Index one document type, filter by caller permissions before retrieval, and return source references. |
| Summarization | Separate application use case that calls a model through an explicit contract | Add a bounded input and a source-linked summary before adding long-document processing. |
| Workflows | Application owns workflow definitions and execution state. Add a worker when jobs need to survive a request or restart. | Start with one durable job with cancellation, recorded progress and idempotent steps. Use a queue and persistent state when the first background workflow ships. |

Build the next slice end to end before adding another provider or host. Expose new backend capabilities through ApiService. Track request duration and failures without logging prompts, documents, credentials or tool payloads. Address the existing NuGet audit warnings before exposing the app beyond local development.

## SQL Server chat storage

The target is a SQL Server database named MCITChatBot. The initial schema is in MCIT.AI.Infrastructure/SqlServer/Migrations/001_ChatStorage.sql. It creates a dedicated chat schema with conversations, ordered messages, and user-owned memory notes. Apply it with a migration identity that can create schemas and tables. Use a separate, least-privilege identity for application reads and writes.

ApiService reads ConnectionStrings:ChatDatabase from its Visual Studio User Secrets for development or from the ConnectionStrings__ChatDatabase environment variable. Blazor connects only to ApiService. Use the same URL and browser profile to reopen your history. Until account sign-in is added, an encrypted browser cookie identifies the owner, so history does not follow the user to another browser or device.

Open History & memory in Chatbot to reopen or delete conversations and save or remove memory notes. The API saves a completed turn as two ordered messages in one transaction. Stop saves the question and any reply received. Failed or disconnected streams are discarded. Follow-up messages use the saved transcript with recent context prioritized. The five most recent saved notes are available as context for future replies. Notes are added explicitly by the user.

Run the initial migration before enabling storage. Without a connection string, chat can run without persistence.
