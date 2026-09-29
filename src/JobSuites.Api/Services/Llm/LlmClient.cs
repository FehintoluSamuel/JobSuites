using Microsoft.Extensions.Options;

namespace JobSuites.Api.Services.Llm;

/// <summary>
/// Thin, dependency-free OpenAI-compatible chat completions client.
///
/// Deliberately not an SDK: this talks the one wire format every compatible
/// endpoint (OpenAI, DeepSeek, OpenRouter, Ollama, LM Studio, ...) shares, so a
/// single feature works against any provider the user already pays for.
/// </summary>
public interface ILlmClient
{
    bool Enabled { get; }

    /// <summary>Runs one completion and returns the assistant message text, or
    /// null when the provider is not configured or the call failed. Null is the
    /// fallback signal — callers degrade to the deterministic path.</summary>
    Task<string?> CompleteAsync(string system, string user, CancellationToken ct);
}

public sealed class LlmClient : ILlmClient
{
    private readonly IHttpClientFactory _http;
    private readonly ILogger<LlmClient> _log;
    private readonly LlmOptions _options;

    public LlmClient(IHttpClientFactory http, IOptions<LlmOptions> options, ILogger<LlmClient> log)
    {
        _http = http;
        _log = log;
        _options = options.Value;
    }

    public bool Enabled => _options.Enabled;

    public async Task<string?> CompleteAsync(string system, string user, CancellationToken ct)
    {
        if (!Enabled) return null;

        var url = _options.BaseUrl!.TrimEnd('/') + "/chat/completions";
        var payload = new
        {
            model = _options.Model,
            messages = new[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user },
            },
            // Low temperature: these passes rephrase and extract existing facts.
            // Creativity would be variability, not quality.
            temperature = 0.2,
        };

        using var client = _http.CreateClient("Llm");
        client.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Content = JsonContent.Create(payload, null, System.Text.Json.JsonSerializerOptions.Default);

        try
        {
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _log.LogWarning("LLM call failed ({Status}: {StatusText}): {Preview}",
                    (int)response.StatusCode, response.ReasonPhrase, body[..Math.Min(body.Length, 300)]);
                return null;
            }

            return await ReadContentAsync(response, ct);
        }
        catch (OperationCanceledException)
        {
            _log.LogWarning("LLM call timed out after {Timeout}s", _options.TimeoutSeconds);
            return null;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "LLM call failed");
            return null;
        }
    }

    private static async Task<string?> ReadContentAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await System.Text.Json.JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!doc.RootElement.TryGetProperty("choices", out var choices) ||
            choices.GetArrayLength() == 0 ||
            !choices[0].TryGetProperty("message", out var message) ||
            !message.TryGetProperty("content", out var content))
        {
            return null;
        }

        return content.ValueKind == System.Text.Json.JsonValueKind.String ? content.GetString() : null;
    }
}