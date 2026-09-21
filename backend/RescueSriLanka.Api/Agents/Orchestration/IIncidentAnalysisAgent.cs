// PLACEHOLDER — owned by Student A (Incident & Disaster Map component).
// The orchestrator only depends on this interface, so Student A can
// replace StubIncidentAnalysisAgent with a real implementation (e.g.
// calling Ollama to classify severity from an incident's text/images)
// without any change needed to AgentOrchestrator or its callers.

namespace RescueSriLanka.Api.Agents.Orchestration
{
    public record IncidentAnalysisResult(string Severity, string Notes);

    public interface IIncidentAnalysisAgent
    {
        Task<IncidentAnalysisResult> AnalyzeAsync(Guid objectiveId);
    }

    // Temporary stand-in so the full workflow runs end-to-end today.
    // Replace this class (not the interface) with real Ollama-backed
    // logic — register the replacement in Program.cs in place of this one.
    public class StubIncidentAnalysisAgent : IIncidentAnalysisAgent
    {
        public Task<IncidentAnalysisResult> AnalyzeAsync(Guid objectiveId)
        {
            return Task.FromResult(new IncidentAnalysisResult(
                Severity: "Unclassified",
                Notes: "Stub result — replace with a real severity classification agent."));
        }
    }
}
