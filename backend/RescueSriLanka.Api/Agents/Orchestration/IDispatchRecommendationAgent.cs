// Shared, provider-independent contract for the Dispatch Recommendation
// Agent. This lives in its own file deliberately — it was previously
// defined inside OllamaDispatchRecommendationAgent.cs, which meant
// deleting that file (when switching providers) also deleted the
// interface GeminiDispatchRecommendationAgent and AgentOrchestrator
// depend on. Keeping the contract separate from every implementation
// means a future provider swap can delete/add implementation files
// freely without breaking anything else.

namespace RescueSriLanka.Api.Agents.Orchestration
{
    public record DispatchRecommendationResult(
        bool Success,
        Guid? RescueTeamId,
        string Reasoning,
        List<string> ToolCallLog);

    public interface IDispatchRecommendationAgent
    {
        Task<DispatchRecommendationResult> RecommendAsync(string requiredSkill);
    }
}
