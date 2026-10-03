using RescueSriLanka.Api.Features.ComponentB.Services;

namespace RescueSriLanka.Api.Tests;

/// <summary>Shared no-op fake for tests that construct HelpRequestService
/// directly and don't care about background triage.</summary>
public sealed class NoOpHelpRequestAnalysisQueue : IHelpRequestAnalysisQueue
{
    public static readonly NoOpHelpRequestAnalysisQueue Instance = new();

    public void Enqueue(Guid helpRequestId)
    {
    }
}
