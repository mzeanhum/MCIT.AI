using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MCIT.AI.Application.Chat;
using Microsoft.Extensions.Options;

namespace MCIT.AI.Infrastructure.Ollama;

public static class OllamaServiceExtensions
{
    public static IServiceCollection AddOllamaChat(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OllamaOptions>()
            .Bind(configuration.GetRequiredSection(OllamaOptions.SectionName))
            .Validate(options => Uri.TryCreate(options.Endpoint, UriKind.Absolute, out Uri? uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                && string.IsNullOrEmpty(uri.UserInfo), "Ollama:Endpoint must be an absolute HTTP(S) URL without credentials.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Model), "Ollama:Model is required.")
            .Validate(options => options.TimeoutSeconds > 0, "Ollama:TimeoutSeconds must be positive.")
            .Validate(options => options.ContextLength >= 2048, "Ollama:ContextLength must be at least 2048.")
            .Validate(options => options.FirstReplyTimeoutSeconds > 0, "Ollama:FirstReplyTimeoutSeconds must be positive.")
            .ValidateOnStart();

#pragma warning disable EXTEXP0001 // Chat generation should not inherit retry and short timeout policies.
        services.AddHttpClient<IChatService, OllamaChatService>((provider, client) =>
        {
            OllamaOptions options = provider.GetRequiredService<IOptions<OllamaOptions>>().Value;
            client.BaseAddress = new Uri(options.Endpoint.TrimEnd('/') + "/");
            client.Timeout = Timeout.InfiniteTimeSpan;
        }).RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
        return services;
    }
}
