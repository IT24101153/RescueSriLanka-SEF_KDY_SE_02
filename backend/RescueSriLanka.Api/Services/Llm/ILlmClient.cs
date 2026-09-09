namespace RescueSriLanka.Api.Services.Llm;

/// <summary>
/// Provider-agnostic view of a text model. Agents depend on this, never on a
/// concrete provider, so swapping Google AI for a local model is one class.
/// </summary>
public interface ILlmClient
{
    /// <summary>True when the client is configured well enough to be called.</summary>
    bool IsConfigured { get; }

    /// <summary>Model identifier, recorded on each agent run for auditability.</summary>
    string ModelName { get; }

    /// <summary>
    /// Sends a prompt and returns the raw text response. When
    /// <paramref name="jsonSchema"/> is supplied the provider is asked to
    /// return JSON matching it.
    /// </summary>
    Task<string> GenerateAsync(
        string systemInstruction,
        string prompt,
        object? jsonSchema = null,
        IReadOnlyList<LlmImage>? images = null,
        CancellationToken ct = default);
}

/// <summary>An image supplied to a multimodal model.</summary>
public record LlmImage(string MimeType, byte[] Data);

/// <summary>Raised when the provider is unreachable, rate limited or rejects the request.</summary>
public class LlmUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);
