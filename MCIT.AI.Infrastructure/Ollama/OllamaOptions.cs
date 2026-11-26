namespace MCIT.AI.Infrastructure.Ollama;

public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";
    public string Endpoint { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int ContextLength { get; set; } = 8192;
    public int TimeoutSeconds { get; set; }
    public int FirstReplyTimeoutSeconds { get; set; } = 60;
    public bool? Think { get; set; }
}
