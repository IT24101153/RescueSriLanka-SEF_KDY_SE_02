using System.Text.Json;
using RescueSriLanka.Api.Features.ComponentA.Agents.IncidentAnalysisAgent;
using RescueSriLanka.Api.Services.Llm;

namespace RescueSriLanka.Api.Features.ComponentA.Agents.Shared;

/// <summary>
/// How Component A's reasoning agents ask the language model. Nothing stands in
/// for the model: when it is unconfigured, out of quota, unreachable or answers
/// with something unreadable, this throws <see cref="LlmUnavailableException"/>
/// and the workflow records the run as failed. The exception's message is shown
/// to the coordinator as written, so it has to read as plain language.
/// </summary>
public static class ModelCall
{
    public const string NotConfigured =
        "No AI model is configured on the server (GoogleAi:ApiKey is missing), so the AI agents cannot run.";

    public const string Unreadable =
        "The AI model returned an answer that could not be read. Try again.";

    public static async Task<T> AskAsync<T>(
        ILlmClient llm, string systemInstruction, string prompt, object schema,
        IReadOnlyList<LlmImage>? images, CancellationToken ct)
    {
        if (!llm.IsConfigured)
        {
            throw new LlmUnavailableException(NotConfigured);
        }

        var raw = await llm.GenerateAsync(systemInstruction, prompt, schema, images, ct);

        try
        {
            return JsonSerializer.Deserialize<T>(raw, AnalysisJson.Options)
                ?? throw new LlmUnavailableException(Unreadable);
        }
        catch (JsonException ex)
        {
            throw new LlmUnavailableException(Unreadable, ex);
        }
    }
}
