namespace RescueSriLanka.Api.Features.ComponentB.Services
{
    // What the Planner Agent's assessment of a help request says, in the shape
    // the review screen shows it.
    public class AiAnalysisResult
    {
        public string Reasoning { get; set; } = string.Empty;
        public string CredibilitySignal { get; set; } = string.Empty; // e.g. "Genuine" / "Uncertain"
        public string SuggestedAction { get; set; } = string.Empty;
    }
}
