namespace JobSuites.Api.Services.Llm;

/// <summary>
/// Configuration for the optional LLM pass, bound from the "Llm" section.
///
/// Everything lives server-side. The web app never sees a key and nothing is
/// persisted to the database. When the section is unset or incomplete the app
/// silently falls back to the deterministic engine — tailoring and ingestion
/// must keep working with no key at all (docs/PRODUCT.md §6.1).
/// </summary>
public sealed class LlmOptions
{
    public const string Section = "Llm";

    /// <summary>Base URL of any OpenAI-compatible chat completions endpoint,
    /// e.g. "https://api.openai.com/v1", ".../v1" for DeepSeek/OpenRouter, or
    /// "http://localhost:11434/v1" for Ollama.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Bearer token for that endpoint. Never sent to the client and
    /// never written to the database.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Model name, e.g. "gpt-4o-mini", "deepseek-chat".</summary>
    public string? Model { get; set; }

    /// <summary>Per-call timeout. A hung upstream must degrade to the
    /// deterministic fallback, not stall the request.</summary>
    public int TimeoutSeconds { get; set; } = 60;

    public bool Enabled =>
        !string.IsNullOrWhiteSpace(BaseUrl) &&
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(Model);
}